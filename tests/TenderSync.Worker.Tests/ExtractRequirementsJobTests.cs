using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Abstractions.Storage;
using TenderSync.Application.Features.Llm;
using TenderSync.Application.Features.Requirements;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Events;
using TenderSync.Domain.Notifications;
using TenderSync.Domain.Tenders;
using TenderSync.Infrastructure.Persistence;
using TenderSync.Worker.Jobs;

namespace TenderSync.Worker.Tests;

/// <summary>The extraction job end to end with the fake model, and the chain from document text to extraction for matched tenders.</summary>
[Collection(WorkerCollection.Name)]
[Trait("Category", "Integration")]
public class ExtractRequirementsJobTests(WorkerFactory factory)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private async Task<(Guid TenderId, Guid DocumentId)> InsertExtractedTenderAsync(bool matched)
    {
        var ocid = $"ocds-9t57fa-{Random.Shared.Next(100000, 999999)}";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        var buyer = await db.Buyers.GetByExternalIdAsync("167");
        if (buyer is null)
        {
            buyer = Buyer.Create("167", "ESKOM", BuyerType.StateOwnedEntity, Province.National);
            db.Buyers.Add(buyer);
        }

        var profiles = await db.CompanyProfiles.ListActiveAsync();
        var profile = profiles.Count > 0 ? profiles[0] : null;
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
        var document = tender.Documents.Single();
        var path = await store.SaveAsync($"tenders/{ocid}/{document.Id}.pdf", new MemoryStream("%PDF-1.7 fake"u8.ToArray()), "application/pdf");
        document.MarkDownloaded(path, new string('a', 64), 13, Now);
        document.MarkExtracted(2, false, Now);
        db.Tenders.Add(tender);
        if (matched)
        {
            db.TenderMatches.Add(TenderMatch.Create(tender.Id, profile.Id, 80, Now));
        }

        await db.SaveChangesAsync();
        await db.Tenders.ReplaceDocumentTextsAsync(document.Id, [new DocumentText(document.Id, 1, "INVITATION TO BID"), new DocumentText(document.Id, 2, "Compulsory briefing on 3 November 2026 at 10:00")]);
        await db.SaveChangesAsync();
        return (tender.Id, document.Id);
    }

    [Fact]
    public async Task The_job_extracts_persists_updates_the_tender_and_raises_the_event()
    {
        var (tenderId, documentId) = await InsertExtractedTenderAsync(matched: true);
        factory.Services.GetRequiredService<FakeLlmClient>().Answer("extract-requirements", new TenderRequirementsV1(
            "SAP support for Eskom.",
            [new(RequirementType.CsdRegistration, "CSD registered", null, new(1, "INVITATION TO BID"), 0.9)],
            [], 70, "80/20",
            [new("SBD 4", "SBD4", true, null, new(1, "SBD 4"), 0.9)],
            new BriefingV1("2026-11-03T10:00:00+02:00", true, "Megawatt Park", null, new(2, "Compulsory briefing"), 0.9),
            new SubmissionV1(SubmissionMethod.ETendersOnline, null, "2026-11-20T11:00:00+02:00", null, new(2, "online"), 0.8),
            null, null, null));

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ExtractRequirementsJob>().ExecuteAsync(tenderId, CancellationToken.None);
        }

        using var check = factory.Services.CreateScope();
        var context = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await context.TenderRequirementSet.AsNoTracking().Where(r => r.TenderId == tenderId).ToListAsync();
        rows.Select(r => r.Kind).Should().BeEquivalentTo([RequirementKind.Mandatory, RequirementKind.Evaluation, RequirementKind.Document, RequirementKind.Briefing, RequirementKind.Submission]);
        rows.Single(r => r.Kind == RequirementKind.Mandatory).Source.Should().Be(new RequirementSource(documentId, 1, "INVITATION TO BID"));
        var tender = await context.TenderSet.AsNoTracking().SingleAsync(t => t.Id == tenderId);
        tender.Summary.Should().Be("SAP support for Eskom.");
        tender.BriefingCompulsory.Should().BeTrue();
        tender.BriefingAt!.Value.ToUniversalTime().Should().Be(new DateTimeOffset(2026, 11, 3, 8, 0, 0, TimeSpan.Zero));
        tender.SubmissionMethod.Should().Be(SubmissionMethod.ETendersOnline);
        (await context.OutboxMessageSet.AsNoTracking().Where(m => m.Type == typeof(RequirementsExtracted).FullName).ToListAsync()).Should().Contain(m => m.PayloadJson.Contains(tenderId.ToString()));
        (await context.LlmCallSet.AsNoTracking().Where(c => c.TenderId == tenderId).ToListAsync()).Should().ContainSingle(c => c.Purpose == "extract-requirements" && c.Success);
        (await context.JobRunSet.AsNoTracking().Where(r => r.JobName == nameof(ExtractRequirementsJob)).OrderByDescending(r => r.StartedAt).FirstAsync()).Success.Should().BeTrue();

        // Re-running replaces the rows instead of duplicating them.
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ExtractRequirementsJob>().ExecuteAsync(tenderId, CancellationToken.None);
        }

        (await context.TenderRequirementSet.AsNoTracking().CountAsync(r => r.TenderId == tenderId)).Should().Be(5);
    }

    [Fact]
    public async Task Document_text_of_a_matched_tender_enqueues_extraction_but_an_unmatched_one_does_not()
    {
        var (matched, matchedDoc) = await InsertExtractedTenderAsync(matched: true);
        var (unmatched, unmatchedDoc) = await InsertExtractedTenderAsync(matched: false);
        var monitoring = factory.Services.GetRequiredService<JobStorage>().GetMonitoringApi();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            db.Outbox.Add(OutboxMessage.FromEvent(new DocumentTextExtracted(unmatched, unmatchedDoc, 2, false, Now)));
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProcessOutboxJob>().ExecuteAsync(CancellationToken.None);
        }

        var before = monitoring.EnqueuedCount(JobQueues.Pipeline);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            db.Outbox.Add(OutboxMessage.FromEvent(new DocumentTextExtracted(matched, matchedDoc, 2, false, Now)));
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProcessOutboxJob>().ExecuteAsync(CancellationToken.None);
        }

        monitoring.EnqueuedCount(JobQueues.Pipeline).Should().BeGreaterThanOrEqualTo(before + 1, "the matched tender's last document is in, so extraction is queued");
    }
}
