using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Notifications;
using TenderSync.Domain.Tenders;
using TenderSync.Infrastructure.Identity;
using TenderSync.Infrastructure.Persistence;
using TenderSync.Worker.Jobs;

namespace TenderSync.Worker.Tests;

/// <summary>WP11 end to end: refresh by ocid, status derivation over time, the three reminder passes, and the one-notification rule for a moved closing date.</summary>
[Collection(WorkerCollection.Name)]
[Trait("Category", "Integration")]
public class StatusJobTests(WorkerFactory factory)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static TenderSnapshot Snapshot(string ocid, DateTimeOffset? closing, string title = "SAP support services", IReadOnlyList<TenderDocumentSnapshot>? documents = null, bool awards = false) => new(
        ocid, ocid[^6..], $"E{ocid[^6..]}", title, "SAP support services for the national grid", "167", "ESKOM",
        ProcurementCategory.Services, ["services"], "open", "Request for Quotation", null, "ZAR", Now.AddDays(-10), closing,
        $"https://www.etenders.gov.za/Home/TenderDetails?Id={ocid[^6..]}", $"{ocid}-release-{closing?.Ticks ?? 0}-{documents?.Count ?? 1}-{awards}", Now.AddDays(-10), "compiled", "{}",
        documents ?? [new TenderDocumentSnapshot($"{ocid}-doc", "Bid Document.pdf", "https://www.etenders.gov.za/home/Download?blobName=x.pdf", "pdf", "en", Now.AddDays(-10), Now.AddDays(-10))], awards);

    private static string NewOcid() => $"ocds-9t57fa-{Random.Shared.Next(100000, 999999)}";

    private async Task EnsurePreferencesAsync()
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = (await users.FindByEmailAsync(WorkerFactory.AdminEmail))!;
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var existing = await db.NotificationPreferences.ListByUserAsync(admin.Id);
        var all = Enum.GetValues<NotificationType>();
        var email = existing.FirstOrDefault(p => p.Channel == NotificationChannel.Email);
        if (email is null)
        {
            db.NotificationPreferences.Add(NotificationPreference.Create(admin.Id, NotificationChannel.Email, "admin@tendersync.test", all));
        }
        else
        {
            email.Update("admin@tendersync.test", all, true, null);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Ingests the snapshot through the real ingestor (so the source of truth is the same code path) and matches it to the first active profile.</summary>
    private async Task<(Guid TenderId, Guid ProfileId)> IngestMatchedAsync(TenderSnapshot snapshot)
    {
        factory.Source.Set(snapshot);
        using var scope = factory.Services.CreateScope();
        var ingestor = scope.ServiceProvider.GetRequiredService<Application.Features.Ingest.TenderIngestor>();
        await ingestor.UpsertAsync(snapshot);

        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var active = await db.CompanyProfiles.ListActiveAsync();
        var profile = active.Count > 0 ? active[0] : null;
        if (profile is null)
        {
            profile = CompanyProfile.Create("Acme Software");
            db.CompanyProfiles.Add(profile);
        }

        var tender = (await db.Tenders.GetByOcidAsync(snapshot.Ocid))!;
        if (await db.TenderMatches.FindAsync(tender.Id, profile.Id) is null)
        {
            db.TenderMatches.Add(TenderMatch.Create(tender.Id, profile.Id, 80, Now));
        }

        await db.SaveChangesAsync();
        return (tender.Id, profile.Id);
    }

    private async Task DrainOutboxAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ProcessOutboxJob>().ExecuteAsync(CancellationToken.None);
    }

    private async Task<List<Notification>> RowsAsync(Guid tenderId, NotificationType type)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.NotificationSet.AsNoTracking().Where(n => n.TenderId == tenderId && n.Type == type).ToListAsync();
    }

    private async Task<Tender> LoadAsync(Guid tenderId)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IAppDbContext>().Tenders.GetWithDetailsAsync(tenderId))!;
    }

    [Fact]
    public async Task Refresh_fetches_matched_tenders_by_ocid_and_a_moved_closing_date_notifies_exactly_once()
    {
        await EnsurePreferencesAsync();
        var ocid = NewOcid();
        var (tenderId, _) = await IngestMatchedAsync(Snapshot(ocid, Now.AddDays(10)));
        await DrainOutboxAsync();
        (await RowsAsync(tenderId, NotificationType.TenderChanged)).Should().BeEmpty("a new tender is not a change");

        // The portal moves the closing date and adds a document.
        factory.Source.Set(Snapshot(ocid, Now.AddDays(17), documents:
        [
            new TenderDocumentSnapshot($"{ocid}-doc", "Bid Document.pdf", "https://www.etenders.gov.za/home/Download?blobName=x.pdf", "pdf", "en", Now.AddDays(-10), Now.AddDays(-10)),
            new TenderDocumentSnapshot($"{ocid}-add", "Addendum 1.pdf", "https://www.etenders.gov.za/home/Download?blobName=y.pdf", "pdf", "en", Now, Now),
        ]));
        var callsBefore = factory.Source.FetchByOcidCalls;

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<RefreshActiveTendersJob>().ExecuteAsync(CancellationToken.None);
        }

        factory.Source.FetchByOcidCalls.Should().BeGreaterThan(callsBefore);
        var tender = await LoadAsync(tenderId);
        tender.ClosingAt.Should().BeCloseTo(Now.AddDays(17), TimeSpan.FromMilliseconds(1), "timestamptz keeps microseconds");
        tender.Documents.Should().HaveCount(2);
        tender.LastCheckedAt.Should().NotBeNull();

        await DrainOutboxAsync();
        var changed = await RowsAsync(tenderId, NotificationType.TenderChanged);
        changed.Should().ContainSingle("one preference, one distinct change").Which.Body.Should().Contain("closing date moved").And.Contain("Addendum 1.pdf");

        // The next morning's refresh sees the same release: no second notification.
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<RefreshActiveTendersJob>().ExecuteAsync(CancellationToken.None);
        }

        await DrainOutboxAsync();
        (await RowsAsync(tenderId, NotificationType.TenderChanged)).Should().HaveCount(1);
        (await LoadAsync(tenderId)).Releases.Should().HaveCount(2);
    }

    [Fact]
    public async Task Refresh_detects_cancellation_and_awards_and_the_status_job_closes_expired_tenders()
    {
        await EnsurePreferencesAsync();
        var cancelledOcid = NewOcid();
        var awardedOcid = NewOcid();
        var expiringOcid = NewOcid();
        var (cancelledId, _) = await IngestMatchedAsync(Snapshot(cancelledOcid, Now.AddDays(10)));
        var (awardedId, _) = await IngestMatchedAsync(Snapshot(awardedOcid, Now.AddDays(-3)));
        // Not the class-level Now: in a full run this test may start long after the class was first instantiated.
        var (expiringId, _) = await IngestMatchedAsync(Snapshot(expiringOcid, DateTimeOffset.UtcNow.AddSeconds(2)));
        await DrainOutboxAsync();

        factory.Source.Set(Snapshot(cancelledOcid, Now.AddDays(10), title: "Cancellation E" + cancelledOcid[^6..]));
        factory.Source.Set(Snapshot(awardedOcid, Now.AddDays(-3), awards: true));
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<RefreshActiveTendersJob>().ExecuteAsync(CancellationToken.None);
        }

        (await LoadAsync(cancelledId)).Status.Should().Be(TenderStatus.Cancelled);
        (await LoadAsync(awardedId)).Status.Should().Be(TenderStatus.Awarded);

        await Task.Delay(TimeSpan.FromSeconds(2.5));
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ComputeStatusJob>().ExecuteAsync(CancellationToken.None);
        }

        var expired = await LoadAsync(expiringId);
        expired.Status.Should().Be(TenderStatus.Closed);
        expired.StatusHistory.Select(h => h.ToStatus).Should().Equal(TenderStatus.ClosingSoon, TenderStatus.Closed);

        await DrainOutboxAsync();
        (await RowsAsync(cancelledId, NotificationType.TenderChanged)).Should().ContainSingle().Which.Body.Should().Contain("cancelled");
        (await RowsAsync(awardedId, NotificationType.AwardPublished)).Should().ContainSingle();
        (await RowsAsync(expiringId, NotificationType.TenderChanged)).Should().ContainSingle().Which.Body.Should().Contain("closed");
    }

    [Fact]
    public async Task Reminders_fire_once_per_bucket_and_skip_dismissed_matches_and_expiring_documents_are_flagged()
    {
        await EnsurePreferencesAsync();
        var soonOcid = NewOcid();
        var dismissedOcid = NewOcid();
        var (soonId, profileId) = await IngestMatchedAsync(Snapshot(soonOcid, Now.AddDays(2)));
        var (dismissedId, _) = await IngestMatchedAsync(Snapshot(dismissedOcid, Now.AddDays(2)));
        Guid documentId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var match = (await db.TenderMatches.FindAsync(dismissedId, profileId))!;
            match.Dismiss("not for us", Now);
            var tender = (await db.Tenders.GetWithDetailsAsync(soonId))!;
            tender.SetBriefing(Now.AddDays(1), true, "Megawatt Park");
            var document = CompanyDocument.Create(profileId, CompanyDocumentType.TaxPin, "Tax PIN 2026", $"companies/{profileId}/{Guid.NewGuid():N}.enc", "v1|test|abc", new string('a', 64), 10, "application/pdf", Now.AddDays(12));
            db.CompanyDocuments.Add(document);
            documentId = document.Id;
            await db.SaveChangesAsync();
        }

        async Task RunRemindersAsync()
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ClosingRemindersJob>().ExecuteAsync(CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<BriefingRemindersJob>().ExecuteAsync(CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<DocumentExpiryCheckJob>().ExecuteAsync(CancellationToken.None);
        }

        await RunRemindersAsync();
        await RunRemindersAsync();

        var closing = await RowsAsync(soonId, NotificationType.ClosingSoon3d);
        closing.Should().ContainSingle("two days away is the 3-day bucket, sent once");
        closing[0].Subject.Should().StartWith("Closing in 2 days");
        (await RowsAsync(dismissedId, NotificationType.ClosingSoon3d)).Should().BeEmpty("dismissed matches get no reminders");
        var briefing = await RowsAsync(soonId, NotificationType.BriefingReminder);
        briefing.Should().ContainSingle();
        briefing[0].NotBefore.Should().BeNull("a compulsory briefing is urgent");
        briefing[0].Body.Should().Contain("Megawatt Park");

        using var check = factory.Services.CreateScope();
        var context = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var expiring = await context.NotificationSet.AsNoTracking().Where(n => n.Type == NotificationType.DocumentExpiring && n.DedupeKey.Contains(documentId.ToString("N"))).ToListAsync();
        expiring.Should().ContainSingle().Which.Subject.Should().Be("Company document expiring in 12 days: Tax PIN 2026");
    }
}
