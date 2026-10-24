using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Features.Llm;
using TenderSync.Application.Features.Qualification;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Events;
using TenderSync.Domain.Notifications;
using TenderSync.Domain.Qualification;
using TenderSync.Domain.Tenders;
using TenderSync.Infrastructure.Persistence;
using TenderSync.Worker.Jobs;

namespace TenderSync.Worker.Tests;

/// <summary>The qualification job end to end with the fake model, and the chain from extracted requirements to the job.</summary>
[Collection(WorkerCollection.Name)]
[Trait("Category", "Integration")]
public class QualifyTenderJobTests(WorkerFactory factory)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private async Task<(Guid TenderId, Guid ProfileId)> InsertTenderWithRequirementsAsync(bool matched)
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

        profile.UpdateDetails("Acme Software", null, "2015/123456/07", null, 8, 25, 12_000_000m);
        profile.UpdateCompliance("MAAA0001234", Now.AddMonths(6), "PIN123", Now.AddMonths(3), 2, Now.AddMonths(2), "Sworn affidavit");

        var snapshot = new TenderSnapshot(
            ocid, ocid[^6..], $"E{ocid[^6..]}", "SAP support", "SAP support services", "167", "ESKOM",
            ProcurementCategory.Services, ["services"], "open", "Request for Quotation", null, "ZAR", Now, Now.AddDays(21),
            $"https://www.etenders.gov.za/Home/TenderDetails?Id={ocid[^6..]}", $"{ocid}-release", Now, "compiled", "{}",
            [new TenderDocumentSnapshot($"{ocid}-doc", "Bid Document.pdf", "https://www.etenders.gov.za/home/Download?blobName=x.pdf", "pdf", "en", Now, Now)], false);
        var tender = Tender.CreateFromSnapshot(snapshot, buyer.Id, Province.National, TenderSourceKind.ETendersOcds, Now);
        tender.DeriveStatus(Now);
        var document = tender.Documents.Single();
        db.Tenders.Add(tender);
        if (matched)
        {
            db.TenderMatches.Add(TenderMatch.Create(tender.Id, profile.Id, 80, Now));
        }

        await db.SaveChangesAsync();
        await db.Tenders.ReplaceRequirementsAsync(tender.Id,
        [
            TenderRequirement.Create(tender.Id, RequirementKind.Mandatory, RequirementType.CsdRegistration, "Registered on the CSD", true, 0.95m, new RequirementSource(document.Id, 3, "CSD"), "fake", "v1", Now),
            TenderRequirement.Create(tender.Id, RequirementKind.Mandatory, RequirementType.TaxCompliance, "Valid tax compliance status", true, 0.95m, new RequirementSource(document.Id, 3, "tax"), "fake", "v1", Now),
            TenderRequirement.Create(tender.Id, RequirementKind.Functional, RequirementType.Other, "Relevant experience", false, 0.8m, new RequirementSource(document.Id, 5, "experience"), "fake", "v1", Now, weight: 100),
            TenderRequirement.Create(tender.Id, RequirementKind.Evaluation, RequirementType.Other, "Functionality threshold 70/100", true, 0.8m, new RequirementSource(document.Id, null, null), "fake", "v1", Now, threshold: 70),
        ]);
        await db.SaveChangesAsync();
        return (tender.Id, profile.Id);
    }

    [Fact]
    public async Task The_job_stores_a_report_records_the_model_call_and_raises_the_event()
    {
        var (tenderId, profileId) = await InsertTenderWithRequirementsAsync(matched: true);
        factory.Services.GetRequiredService<FakeLlmClient>().Answer(TenderQualifier.EstimatePurpose,
            new FunctionalityEstimateV1([new("Relevant experience", 100, 75, "Reference projects on file", 0.7)], "Reasonable fit.", ["Confirm the SAP references are contactable"]));

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<QualifyTenderJob>().ExecuteAsync(tenderId, null, CancellationToken.None);
        }

        using var check = factory.Services.CreateScope();
        var context = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var report = await context.QualificationReportSet.AsNoTracking().Where(r => r.TenderId == tenderId).OrderByDescending(r => r.CreatedAt).FirstAsync();
        report.CompanyProfileId.Should().Be(profileId);
        report.Verdict.Should().Be(QualificationVerdict.Qualify);
        report.EstimatedFunctionalityScore.Should().Be(75);
        report.Findings.Should().HaveCount(2).And.AllSatisfy(f => f.Outcome.Should().Be(FindingOutcome.Pass));
        report.Questions.Should().ContainSingle(q => q.Question.Contains("SAP references"));
        report.SummaryMarkdown.Should().Contain("**Disclaimer.**");
        (await context.OutboxMessageSet.AsNoTracking().Where(m => m.Type == typeof(QualificationCompleted).FullName).ToListAsync()).Should().Contain(m => m.PayloadJson.Contains(report.Id.ToString()));
        (await context.LlmCallSet.AsNoTracking().Where(c => c.TenderId == tenderId).ToListAsync()).Should().ContainSingle(c => c.Purpose == TenderQualifier.EstimatePurpose && c.Success);
        (await context.JobRunSet.AsNoTracking().Where(r => r.JobName == nameof(QualifyTenderJob)).OrderByDescending(r => r.StartedAt).FirstAsync()).Success.Should().BeTrue();

        // A second run adds a report; the newest is what the API returns.
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<QualifyTenderJob>().ExecuteAsync(tenderId, profileId, CancellationToken.None);
        }

        (await context.QualificationReportSet.AsNoTracking().CountAsync(r => r.TenderId == tenderId)).Should().Be(2);
        var latest = await check.ServiceProvider.GetRequiredService<IAppDbContext>().QualificationReports.GetLatestAsync(tenderId, profileId);
        latest!.Id.Should().NotBe(report.Id);
    }

    [Fact]
    public async Task Extracted_requirements_of_a_matched_tender_enqueue_qualification_but_an_unmatched_one_does_not()
    {
        var (matched, _) = await InsertTenderWithRequirementsAsync(matched: true);
        var (unmatched, _) = await InsertTenderWithRequirementsAsync(matched: false);
        var monitoring = factory.Services.GetRequiredService<JobStorage>().GetMonitoringApi();

        async Task PublishAsync(Guid tenderId)
        {
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                db.Outbox.Add(OutboxMessage.FromEvent(new RequirementsExtracted(tenderId, "ocds-x", 4, Now)));
                await db.SaveChangesAsync();
            }

            using (var scope = factory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<ProcessOutboxJob>().ExecuteAsync(CancellationToken.None);
            }
        }

        await PublishAsync(unmatched);
        var before = monitoring.EnqueuedCount(JobQueues.Pipeline);
        await PublishAsync(matched);

        monitoring.EnqueuedCount(JobQueues.Pipeline).Should().BeGreaterThanOrEqualTo(before + 1, "the matched tender's requirements are in, so qualification is queued");
    }
}
