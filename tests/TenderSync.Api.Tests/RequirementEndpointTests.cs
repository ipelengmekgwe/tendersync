using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Features.Requirements.Commands.RequestExtraction;
using TenderSync.Application.Features.Requirements.Queries.GetTenderRequirements;
using TenderSync.Application.Features.Tenders;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Events;
using TenderSync.Domain.Tenders;
using TenderSync.Infrastructure.Persistence;

namespace TenderSync.Api.Tests;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class RequirementEndpointTests(ApiFactory factory)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private async Task<(Guid TenderId, Guid DocumentId)> InsertTenderAsync(bool withText)
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

        var snapshot = new TenderSnapshot(
            ocid, ocid[^6..], $"E{ocid[^6..]}", "SAP support", "SAP support services", "167", "ESKOM",
            ProcurementCategory.Services, ["services"], "open", "Request for Quotation", null, "ZAR", Now, Now.AddDays(21),
            $"https://www.etenders.gov.za/Home/TenderDetails?Id={ocid[^6..]}", $"{ocid}-release", Now, "compiled", "{}",
            [new TenderDocumentSnapshot($"{ocid}-doc", "Bid Document.pdf", "https://www.etenders.gov.za/home/Download?blobName=x.pdf", "pdf", "en", Now, Now)], false);
        var tender = Tender.CreateFromSnapshot(snapshot, buyer.Id, Province.National, TenderSourceKind.ETendersOcds, Now);
        tender.DeriveStatus(Now);
        var document = tender.Documents.Single();
        if (withText)
        {
            document.MarkDownloaded($"tenders/{ocid}/{document.Id}.pdf", new string('a', 64), 100, Now);
            document.MarkExtracted(2, false, Now);
        }

        db.Tenders.Add(tender);
        await db.SaveChangesAsync();
        return (tender.Id, document.Id);
    }

    [Fact]
    public async Task Requirements_are_grouped_with_sources_and_verify_flags()
    {
        var (tenderId, documentId) = await InsertTenderAsync(withText: true);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var tender = (await db.Tenders.GetWithDetailsAsync(tenderId))!;
            tender.SetSummary("Support of SAP for three years.");
            await db.Tenders.ReplaceRequirementsAsync(tenderId,
            [
                TenderRequirement.Create(tenderId, RequirementKind.Mandatory, RequirementType.CsdRegistration, "Registered on the CSD", true, 0.95m, new RequirementSource(documentId, 3, "CSD"), "claude-opus-5-5", "v1", Now),
                TenderRequirement.Create(tenderId, RequirementKind.Document, RequirementType.Other, "SBD 6.1: Preference points", true, 0.4m, new RequirementSource(documentId, null, null), "claude-opus-5-5", "v1", Now),
                TenderRequirement.Create(tenderId, RequirementKind.Evaluation, RequirementType.Other, "Functionality threshold 70/100", true, 0.8m, new RequirementSource(documentId, null, null), "claude-opus-5-5", "v1", Now, threshold: 70),
            ]);
            await db.SaveChangesAsync();
        }

        using var client = await factory.CreateViewerClientAsync();
        var dto = await client.GetFromJsonAsync<TenderRequirementsDto>($"/api/v1/tenders/{tenderId}/requirements");

        dto!.Summary.Should().Be("Support of SAP for three years.");
        dto.Total.Should().Be(3);
        dto.NeedsVerification.Should().Be(2, "the low-confidence form and the page-less evaluation row");
        dto.Model.Should().Be("claude-opus-5-5");
        dto.Mandatory.Should().ContainSingle().Which.Should().Match<RequirementDto>(r => r.SourceDocumentTitle == "Bid Document.pdf" && r.SourcePage == 3 && !r.NeedsVerification);
        dto.Documents.Single().NeedsVerification.Should().BeTrue();
        dto.Functional.Single().Threshold.Should().Be(70);

        var detail = await client.GetFromJsonAsync<TenderDetailDto>($"/api/v1/tenders/{tenderId}");
        detail!.Summary.Should().Be("Support of SAP for three years.");
        (await client.GetAsync($"/api/v1/tenders/{Guid.CreateVersion7()}/requirements")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reviewers_queue_extraction_through_the_outbox_and_pending_documents_are_queued_for_download()
    {
        var (ready, _) = await InsertTenderAsync(withText: true);
        var (pending, _) = await InsertTenderAsync(withText: false);

        using var viewer = await factory.CreateViewerClientAsync();
        (await viewer.PostAsync($"/api/v1/tenders/{ready}/extract", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var admin = await factory.CreateAdminClientAsync();
        var response = await admin.PostAsync($"/api/v1/tenders/{ready}/extract", null);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<RequestExtractionResponse>();
        body!.ExtractionQueued.Should().BeTrue();
        body.DocumentsWithText.Should().Be(1);
        body.DocumentsQueuedForDownload.Should().Be(0);

        var pendingBody = await (await admin.PostAsync($"/api/v1/tenders/{pending}/extract", null)).Content.ReadFromJsonAsync<RequestExtractionResponse>();
        pendingBody!.ExtractionQueued.Should().BeFalse("nothing has text yet");
        pendingBody.DocumentsQueuedForDownload.Should().Be(1, "the download runs first and extraction chains after it");

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await context.OutboxMessageSet.AsNoTracking().Where(m => m.Type == typeof(TenderExtractionRequested).FullName).ToListAsync()).Should().Contain(m => m.PayloadJson.Contains(ready.ToString()));
        (await context.OutboxMessageSet.AsNoTracking().Where(m => m.Type == typeof(TenderDocumentsRequested).FullName).ToListAsync()).Should().Contain(m => m.PayloadJson.Contains(pending.ToString()));
    }
}
