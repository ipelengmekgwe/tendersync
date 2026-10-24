using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Features.Classification;
using TenderSync.Application.Features.Llm;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Events;
using TenderSync.Domain.Notifications;
using TenderSync.Domain.Tenders;
using TenderSync.Infrastructure.Persistence;
using TenderSync.Worker.Jobs;

namespace TenderSync.Worker.Tests;

/// <summary>The classify job end to end with the fake model, and the outbox trigger that enqueues it for new tenders.</summary>
[Collection(WorkerCollection.Name)]
[Trait("Category", "Integration")]
public class ClassifyTenderJobTests(WorkerFactory factory)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private async Task<Guid> InsertTenderAsync(string title, string description)
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

        if ((await db.CompanyProfiles.ListActiveAsync()).Count == 0)
        {
            var profile = CompanyProfile.Create("Acme Software");
            profile.UpdateCapabilities(new CompanyCapabilities { CategoryCodes = ["SW_DEV"] });
            db.CompanyProfiles.Add(profile);
        }

        var snapshot = new TenderSnapshot(
            ocid, ocid[^6..], $"E{ocid[^6..]}", title, description, "167", "ESKOM",
            ProcurementCategory.Services, ["services"], "open", "Request for Quotation", null, "ZAR", Now, Now.AddDays(21),
            $"https://www.etenders.gov.za/Home/TenderDetails?Id={ocid[^6..]}", $"{ocid}-release", Now, "compiled", "{}", [], false);
        var tender = Tender.CreateFromSnapshot(snapshot, buyer.Id, Province.National, TenderSourceKind.ETendersOcds, Now);
        tender.DeriveStatus(Now);
        db.Tenders.Add(tender);
        await db.SaveChangesAsync();
        return tender.Id;
    }

    [Fact]
    public async Task The_job_classifies_matches_bills_and_records_a_run()
    {
        var tenderId = await InsertTenderAsync("Custom software development for a permit system", "Develop and support a permit management system.");
        factory.Services.GetRequiredService<FakeLlmClient>().Answer("classify", new ClassificationAnswer([new("SW_DEV", 0.9)], 85, true, "Software development.", []));

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ClassifyTenderJob>().ExecuteAsync(tenderId, CancellationToken.None);
        }

        using var check = factory.Services.CreateScope();
        var context = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var classification = await context.TenderClassificationSet.AsNoTracking().SingleAsync(c => c.TenderId == tenderId);
        classification.Stage.Should().Be(ClassificationStage.Llm);
        classification.Relevance.Should().Be(85);
        classification.CategoryCodes.Should().Equal("SW_DEV");
        (await context.TenderMatchSet.AsNoTracking().Where(m => m.TenderId == tenderId).ToListAsync()).Should().ContainSingle(m => m.Relevance == 85 && m.Status == MatchStatus.New);
        (await context.LlmCallSet.AsNoTracking().Where(c => c.TenderId == tenderId).ToListAsync()).Should().ContainSingle(c => c.Purpose == "classify" && c.Provider == "fake" && c.Success);
        (await context.OutboxMessageSet.AsNoTracking().Where(m => m.Type == typeof(TenderMatched).FullName).ToListAsync()).Should().Contain(m => m.PayloadJson.Contains(tenderId.ToString()));
        (await context.JobRunSet.AsNoTracking().Where(r => r.JobName == nameof(ClassifyTenderJob)).OrderByDescending(r => r.StartedAt).FirstAsync()).Success.Should().BeTrue();
    }

    [Fact]
    public async Task A_non_it_tender_stops_at_the_keyword_stage_without_a_model_call()
    {
        var tenderId = await InsertTenderAsync("Replacement of UPS units at head office", "Supply and install UPS units.");
        var fake = factory.Services.GetRequiredService<FakeLlmClient>();
        var before = fake.Requests.Count;

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ClassifyTenderJob>().ExecuteAsync(tenderId, CancellationToken.None);
        }

        fake.Requests.Count.Should().Be(before);
        using var check = factory.Services.CreateScope();
        var context = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var classification = await context.TenderClassificationSet.AsNoTracking().SingleAsync(c => c.TenderId == tenderId);
        classification.Stage.Should().Be(ClassificationStage.Keyword);
        classification.Relevance.Should().Be(0);
        (await context.TenderMatchSet.AsNoTracking().AnyAsync(m => m.TenderId == tenderId)).Should().BeFalse();
    }

    [Fact]
    public async Task A_first_seen_event_enqueues_the_classify_job_from_the_outbox()
    {
        var tenderId = await InsertTenderAsync("Anything", "Anything");
        var monitoring = factory.Services.GetRequiredService<JobStorage>().GetMonitoringApi();
        var before = monitoring.EnqueuedCount(JobQueues.Pipeline);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            db.Outbox.Add(OutboxMessage.FromEvent(new TenderFirstSeen(tenderId, "ocds-9t57fa-000001", Now)));
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProcessOutboxJob>().ExecuteAsync(CancellationToken.None);
        }

        monitoring.EnqueuedCount(JobQueues.Pipeline).Should().BeGreaterThanOrEqualTo(before + 1, "the TenderFirstSeen handler queues ClassifyTenderJob; the tender insert itself also raised TenderFirstSeen");
    }
}
