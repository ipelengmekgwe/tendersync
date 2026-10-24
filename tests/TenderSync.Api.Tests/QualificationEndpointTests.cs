using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Features.Qualification.Commands.RequestQualification;
using TenderSync.Application.Features.Qualification.Queries.GetTenderQualification;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Events;
using TenderSync.Domain.Qualification;
using TenderSync.Domain.Tenders;
using TenderSync.Infrastructure.Persistence;

namespace TenderSync.Api.Tests;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class QualificationEndpointTests(ApiFactory factory)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private async Task<(Guid TenderId, Guid DocumentId, Guid ProfileId)> InsertTenderAsync()
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
        await db.SaveChangesAsync();
        return (tender.Id, tender.Documents.Single().Id, profile.Id);
    }

    [Fact]
    public async Task Qualification_needs_requirements_is_queued_through_the_outbox_and_the_newest_report_is_returned()
    {
        var (tenderId, documentId, profileId) = await InsertTenderAsync();
        using var viewer = await factory.CreateViewerClientAsync();
        using var admin = await factory.CreateAdminClientAsync();

        (await viewer.GetAsync($"/api/v1/tenders/{tenderId}/qualification")).StatusCode.Should().Be(HttpStatusCode.NotFound, "no run yet");
        (await viewer.PostAsync($"/api/v1/tenders/{tenderId}/qualify", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PostAsync($"/api/v1/tenders/{tenderId}/qualify", null)).StatusCode.Should().Be(HttpStatusCode.Conflict, "nothing has been extracted");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            await db.Tenders.ReplaceRequirementsAsync(tenderId,
            [
                TenderRequirement.Create(tenderId, RequirementKind.Mandatory, RequirementType.CsdRegistration, "Registered on the CSD", true, 0.95m, new RequirementSource(documentId, 3, "CSD"), "claude-opus-5-5", "v1", Now),
            ]);
            await db.SaveChangesAsync();
        }

        var response = await admin.PostAsync($"/api/v1/tenders/{tenderId}/qualify?companyProfileId={profileId}", null);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<RequestQualificationResponse>();
        body!.Queued.Should().BeTrue();
        body.RequirementCount.Should().Be(1);
        body.CompanyProfileId.Should().Be(profileId);
        (await admin.PostAsync($"/api/v1/tenders/{tenderId}/qualify?companyProfileId={Guid.CreateVersion7()}", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await context.OutboxMessageSet.AsNoTracking().Where(m => m.Type == typeof(TenderQualificationRequested).FullName).ToListAsync()).Should().Contain(m => m.PayloadJson.Contains(tenderId.ToString()));

            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            db.QualificationReports.Add(QualificationReport.Create(tenderId, profileId, QualificationVerdict.NotQualify, 20, [], [], [], null, [], [], "# old", "rules-only", "v1", Now.AddMinutes(-5)));
            await db.SaveChangesAsync();
            db.QualificationReports.Add(QualificationReport.Create(tenderId, profileId, QualificationVerdict.Partial, 64,
                [new(null, RequirementType.CsdRegistration, "Registered on the CSD", FindingOutcome.Pass, "CSD on file.", 0.95m)], [], [], 72, [],
                [new("Who attends the briefing?", "Compulsory.", null)], "# Can we bid?\n\n> **Disclaimer.** ...", "claude-opus-5-5", "v1", Now));
            await db.SaveChangesAsync();
        }

        var report = await viewer.GetFromJsonAsync<QualificationReportDto>($"/api/v1/tenders/{tenderId}/qualification");
        report!.Verdict.Should().Be(QualificationVerdict.Partial, "the newest report wins");
        report.Score.Should().Be(64);
        report.CompanyName.Should().NotBeNullOrEmpty();
        report.EstimatedFunctionalityScore.Should().Be(72);
        report.Findings.Should().ContainSingle(f => f.Outcome == FindingOutcome.Pass);
        report.Questions.Should().ContainSingle();
        report.SummaryMarkdown.Should().StartWith("# Can we bid?");
        (await viewer.GetFromJsonAsync<QualificationReportDto>($"/api/v1/tenders/{tenderId}/qualification?companyProfileId={profileId}"))!.Id.Should().Be(report.Id);
        (await viewer.GetAsync($"/api/v1/tenders/{tenderId}/qualification?companyProfileId={Guid.CreateVersion7()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
