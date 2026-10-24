using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Abstractions.Storage;
using TenderSync.Application.Features.Documents;
using TenderSync.Application.Features.Documents.Commands.RequestDocumentDownload;
using TenderSync.Application.Features.Documents.Queries.GetDocumentText;
using TenderSync.Application.Features.Tenders;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Events;
using TenderSync.Domain.Tenders;
using TenderSync.Infrastructure.Persistence;

namespace TenderSync.Api.Tests;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class DocumentEndpointTests(ApiFactory factory)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private async Task<(Guid TenderId, Guid DocumentId, string Ocid)> InsertTenderWithDocumentAsync()
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
            ocid, ocid[^6..], $"E{ocid[^6..]}", "Network equipment", "Supply of network switches", "167", "ESKOM",
            ProcurementCategory.Goods, ["goods"], "open", "Request for Quotation", null, "ZAR", Now, Now.AddDays(21),
            $"https://www.etenders.gov.za/Home/TenderDetails?Id={ocid[^6..]}", $"{ocid}-release", Now, "compiled", "{}",
            [new TenderDocumentSnapshot($"{ocid}-doc", "Bid Document", "https://www.etenders.gov.za/home/Download?blobName=x.pdf&downloadedFileName=Bid%20Document.pdf", "pdf", "en", Now, Now)],
            false);

        var tender = Tender.CreateFromSnapshot(snapshot, buyer.Id, Province.National, TenderSourceKind.ETendersOcds, Now);
        tender.DeriveStatus(Now);
        db.Tenders.Add(tender);
        await db.SaveChangesAsync();
        return (tender.Id, tender.Documents.Single().Id, ocid);
    }

    [Fact]
    public async Task Documents_are_listed_and_a_pending_document_cannot_be_downloaded_yet()
    {
        var (tenderId, documentId, _) = await InsertTenderWithDocumentAsync();
        using var client = await factory.CreateViewerClientAsync();

        var list = await client.GetFromJsonAsync<List<TenderDocumentDto>>($"/api/v1/tenders/{tenderId}/documents");
        list.Should().ContainSingle().Which.Should().Match<TenderDocumentDto>(d => d.Id == documentId && d.Status == DocumentStatus.Pending && !d.Stored);

        var download = await client.GetAsync($"/api/v1/tenders/{tenderId}/documents/{documentId}/download");
        download.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await download.Content.ReadAsStringAsync()).Should().Contain("etenders.gov.za", "the problem names the original URL");

        (await client.GetAsync($"/api/v1/tenders/{Guid.CreateVersion7()}/documents")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/v1/tenders/{tenderId}/documents/{Guid.CreateVersion7()}/download")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_stored_document_streams_back_with_its_content_type_and_text()
    {
        var (tenderId, documentId, ocid) = await InsertTenderWithDocumentAsync();
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\n% fake bid pack\n%%EOF");
        using (var scope = factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
            var path = await store.SaveAsync(DocumentFormats.StoragePath(ocid, documentId, "pdf"), new MemoryStream(bytes), "application/pdf");

            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var document = (await db.Tenders.GetDocumentAsync(documentId))!;
            document.MarkDownloaded(path, new string('a', 64), bytes.Length, Now);
            document.MarkExtracted(2, false, Now);
            await db.Tenders.ReplaceDocumentTextsAsync(documentId, [new DocumentText(documentId, 1, "Page one text"), new DocumentText(documentId, 2, "Page two text")]);
            await db.SaveChangesAsync();
        }

        using var client = await factory.CreateViewerClientAsync();

        var download = await client.GetAsync($"/api/v1/tenders/{tenderId}/documents/{documentId}/download");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        download.Content.Headers.ContentDisposition!.FileName.Should().Contain("Bid Document.pdf");
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);

        var text = await client.GetFromJsonAsync<DocumentTextDto>($"/api/v1/tenders/{tenderId}/documents/{documentId}/text");
        text!.Status.Should().Be(DocumentStatus.Extracted);
        text.PageCount.Should().Be(2);
        text.Pages.Select(p => p.Text).Should().Equal("Page one text", "Page two text");

        var detail = await client.GetFromJsonAsync<TenderDetailDto>($"/api/v1/tenders/{tenderId}");
        detail!.Documents.Single().Should().Match<TenderDocumentDto>(d => d.Stored && d.SizeBytes == bytes.Length && d.ExtractedAt != null);
    }

    [Fact]
    public async Task Reviewers_can_queue_a_download_which_raises_the_event_through_the_outbox()
    {
        var (tenderId, _, _) = await InsertTenderWithDocumentAsync();

        using var viewer = await factory.CreateViewerClientAsync();
        (await viewer.PostAsync($"/api/v1/tenders/{tenderId}/documents/download", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var admin = await factory.CreateAdminClientAsync();
        var response = await admin.PostAsync($"/api/v1/tenders/{tenderId}/documents/download", null);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<RequestDocumentDownloadResponse>();
        body!.QueuedDocuments.Should().Be(1);
        body.TotalDocuments.Should().Be(1);

        (await admin.PostAsync($"/api/v1/tenders/{Guid.CreateVersion7()}/documents/download", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outbox = await context.OutboxMessageSet.AsNoTracking().Where(m => m.Type == typeof(TenderDocumentsRequested).FullName).ToListAsync();
        outbox.Should().Contain(m => m.PayloadJson.Contains(tenderId.ToString()), "the Worker's outbox job turns the event into DownloadTenderDocumentsJob");
        var audit = await context.AuditLogSet.AsNoTracking().Where(a => a.Action == nameof(RequestDocumentDownloadCommand)).ToListAsync();
        audit.Should().NotBeEmpty("commands are audited");
    }
}
