using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Features.Notifications;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Events;
using TenderSync.Domain.Notifications;
using TenderSync.Domain.Tenders;
using TenderSync.Infrastructure.Identity;
using TenderSync.Infrastructure.Persistence;
using TenderSync.Worker.Jobs;

namespace TenderSync.Worker.Tests;

/// <summary>The outbox → dispatcher → job → fake channel path for every type, dedupe across the real database, and the sweeper and digest jobs.</summary>
[Collection(WorkerCollection.Name)]
[Trait("Category", "Integration")]
public class NotificationJobTests(WorkerFactory factory)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private async Task<Guid> EnsurePreferencesAsync(params NotificationType[] types)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = (await users.FindByEmailAsync(WorkerFactory.AdminEmail))!;
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var existing = await db.NotificationPreferences.ListByUserAsync(admin.Id);
        var all = Enum.GetValues<NotificationType>();
        foreach (var (channel, address) in new[] { (NotificationChannel.Email, "admin@tendersync.test"), (NotificationChannel.Telegram, "123456") })
        {
            var preference = existing.FirstOrDefault(p => p.Channel == channel);
            if (preference is null)
            {
                db.NotificationPreferences.Add(NotificationPreference.Create(admin.Id, channel, address, types.Length == 0 ? all : types));
            }
            else
            {
                preference.Update(address, types.Length == 0 ? all : types, true, null);
            }
        }

        await db.SaveChangesAsync();
        return admin.Id;
    }

    private async Task<(Guid TenderId, Guid ProfileId)> InsertMatchedTenderAsync()
    {
        var ocid = $"ocds-9t57fa-{Random.Shared.Next(100000, 999999)}";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var buyer = await db.Buyers.GetByExternalIdAsync("167");
        if (buyer is null)
        {
            buyer = Buyer.Create("167", "ESKOM", BuyerType.StateOwnedEntity, Province.National);
            db.Buyers.Add(buyer);
        }

        var active = await db.CompanyProfiles.ListActiveAsync();
        var profile = active.Count > 0 ? active[0] : null;
        if (profile is null)
        {
            profile = CompanyProfile.Create("Acme Software");
            db.CompanyProfiles.Add(profile);
        }

        var snapshot = new TenderSnapshot(
            ocid, ocid[^6..], $"E{ocid[^6..]}", "SAP support", "SAP support services", "167", "ESKOM",
            ProcurementCategory.Services, ["services"], "open", "Request for Quotation", null, "ZAR", Now, Now.AddDays(21),
            $"https://www.etenders.gov.za/Home/TenderDetails?Id={ocid[^6..]}", $"{ocid}-release", Now, "compiled", "{}",
            [new TenderDocumentSnapshot($"{ocid}-doc", "Bid Document.pdf", "https://www.etenders.gov.za/home/Download?blobName=x.pdf", "pdf", "en", Now, Now)], false);
        var tender = Tender.CreateFromSnapshot(snapshot, buyer.Id, Province.National, TenderSourceKind.ETendersOcds, Now);
        tender.DeriveStatus(Now);
        db.Tenders.Add(tender);
        db.TenderMatches.Add(TenderMatch.Create(tender.Id, profile.Id, 80, Now));
        await db.SaveChangesAsync();
        return (tender.Id, profile.Id);
    }

    private async Task PublishAsync(Domain.Common.IDomainEvent domainEvent)
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            db.Outbox.Add(OutboxMessage.FromEvent(domainEvent));
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProcessOutboxJob>().ExecuteAsync(CancellationToken.None);
        }
    }

    private async Task<List<Notification>> RowsAsync(Guid? tenderId, NotificationType type)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.NotificationSet.AsNoTracking().Where(n => n.TenderId == tenderId && n.Type == type).OrderBy(n => n.Channel).ToListAsync();
    }

    [Fact]
    public async Task A_match_event_creates_one_row_per_channel_queues_the_send_job_and_the_job_delivers_through_the_channel()
    {
        await EnsurePreferencesAsync();
        var (tenderId, profileId) = await InsertMatchedTenderAsync();
        var monitoring = factory.Services.GetRequiredService<JobStorage>().GetMonitoringApi();
        var before = monitoring.EnqueuedCount(JobQueues.Notify);

        await PublishAsync(new TenderMatched(tenderId, profileId, Guid.CreateVersion7(), 80, Now));
        await PublishAsync(new TenderMatched(tenderId, profileId, Guid.CreateVersion7(), 80, Now));

        var rows = await RowsAsync(tenderId, NotificationType.NewMatch);
        rows.Should().HaveCount(2, "email and Telegram, once each despite the event arriving twice");
        rows.Select(r => r.Channel).Should().BeEquivalentTo([NotificationChannel.Email, NotificationChannel.Telegram]);
        rows.Should().AllSatisfy(r => r.Status.Should().Be(NotificationStatus.Pending));
        rows.Single(r => r.Channel == NotificationChannel.Email).Subject.Should().StartWith("New match: E");
        monitoring.EnqueuedCount(JobQueues.Notify).Should().BeGreaterThanOrEqualTo(before + 2);

        var email = factory.Channels[NotificationChannel.Email];
        var sentBefore = email.Sent.Count;
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SendNotificationJob>().ExecuteAsync(rows.Single(r => r.Channel == NotificationChannel.Email).Id, CancellationToken.None);
        }

        email.Sent.Count.Should().Be(sentBefore + 1);
        email.Sent[^1].Recipient.Should().Be("admin@tendersync.test");
        email.Sent[^1].ActionUrl.Should().Contain($"/tenders/{tenderId}");
        (await RowsAsync(tenderId, NotificationType.NewMatch)).Single(r => r.Channel == NotificationChannel.Email).Status.Should().Be(NotificationStatus.Sent);
    }

    [Fact]
    public async Task Every_event_driven_type_produces_a_message_for_a_matched_tender()
    {
        await EnsurePreferencesAsync();
        var (tenderId, profileId) = await InsertMatchedTenderAsync();
        Guid reportId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var report = Domain.Qualification.QualificationReport.Create(tenderId, profileId, QualificationVerdict.Partial, 64, [], [new("CSD report", null, "missing", null)], [], 72, [], [new("Who attends?", "briefing", null)], "# report", "rules-only", "v1", Now);
            db.QualificationReports.Add(report);
            await db.SaveChangesAsync();
            reportId = report.Id;
        }

        await PublishAsync(new QualificationCompleted(tenderId, profileId, reportId, QualificationVerdict.Partial, 64, Now));
        await PublishAsync(new TenderChanged(tenderId, "ocds-x", new ReleaseDiff(false, [$"doc-new"], [], [], Now.AddDays(21), Now.AddDays(28), false, false, true), Now));
        await PublishAsync(new TenderStatusChanged(tenderId, "ocds-x", TenderStatus.Open, TenderStatus.Cancelled, "withdrawn", Now));
        await PublishAsync(new PipelineFailure("etenders", "3 consecutive failures", null, Now));

        var qualification = await RowsAsync(tenderId, NotificationType.QualificationReady);
        qualification.Should().HaveCount(2);
        qualification[0].Body.Should().Contain("CSD report").And.Contain("Partial");
        var changed = await RowsAsync(tenderId, NotificationType.TenderChanged);
        changed.Should().HaveCount(4, "the new document and the cancellation are distinct changes, on two channels");
        changed.Select(c => c.Body).Should().Contain(b => b.Contains("1 new document")).And.Contain(b => b.Contains("cancelled"));
        (await RowsAsync(tenderId, NotificationType.AwardPublished)).Should().HaveCount(2);
        var failures = await RowsAsync(null, NotificationType.PipelineFailure);
        failures.Should().Contain(f => f.Body.Contains("3 consecutive failures"));
        failures.Should().AllSatisfy(f => f.NotBefore.Should().BeNull("pipeline failures are urgent"));
    }

    [Fact]
    public async Task The_sweeper_delivers_rows_created_without_a_job_and_the_digest_goes_only_to_subscribers()
    {
        var userId = await EnsurePreferencesAsync();
        var telegram = factory.Channels[NotificationChannel.Telegram];
        Guid testId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var row = Notification.Create(NotificationType.Test, NotificationChannel.Telegram, "123456", "Test", "Body", null, null, Guid.NewGuid().ToString("N"), null);
            db.Notifications.Add(row);
            await db.SaveChangesAsync();
            testId = row.Id;
        }

        // Rows younger than the sweep threshold are left to their own job; backdate this one.
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.NotificationSet.Where(n => n.Id == testId).ExecuteUpdateAsync(s => s.SetProperty(n => n.CreatedAt, Now.AddMinutes(-5)));
        }

        var sentBefore = telegram.Sent.Count;
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SweepNotificationsJob>().ExecuteAsync(CancellationToken.None);
        }

        telegram.Sent.Count.Should().BeGreaterThanOrEqualTo(sentBefore + 1);
        telegram.Sent.Should().Contain(m => m.Recipient == "123456" && m.Subject == "Test");

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DailyDigestJob>().ExecuteAsync(CancellationToken.None);
        }

        var digests = await RowsAsync(null, NotificationType.DailyDigest);
        digests.Should().HaveCount(2, "both preferences subscribe to every type, one digest per channel per day");
        digests.Should().AllSatisfy(d => d.Recipient.Should().BeOneOf("admin@tendersync.test", "123456"));
        digests[0].Subject.Should().StartWith("TenderSync digest for");

        // Running the digest again the same day adds nothing.
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DailyDigestJob>().ExecuteAsync(CancellationToken.None);
        }

        (await RowsAsync(null, NotificationType.DailyDigest)).Should().HaveCount(2);
        userId.Should().NotBeEmpty();
    }
}
