using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Models;
using TenderSync.Application.Features.Tenders;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Api.Tests;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class TenderEndpointTests(ApiFactory factory)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private async Task<(Guid Id, string Ocid)> InsertTenderAsync(string description, DateTimeOffset? closingAt = null)
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
            ocid, ocid[^6..], $"E{ocid[^6..]}", description.Length > 80 ? description[..80] : description, description,
            "167", "ESKOM", ProcurementCategory.Services, ["services"], "open", "Request for Quotation", 0, "ZAR",
            Now, closingAt ?? Now.AddDays(21), $"https://www.etenders.gov.za/Home/TenderDetails?Id={ocid[^6..]}",
            $"{ocid}-release", Now, "compiled", $$"""{"ocid":"{{ocid}}"}""",
            [new TenderDocumentSnapshot($"{ocid}-doc", "Bid Document.pdf", "https://www.etenders.gov.za/home/Download?blobName=x.pdf", "pdf", "en", Now, Now)],
            false);

        var tender = Tender.CreateFromSnapshot(snapshot, buyer.Id, Province.National, TenderSourceKind.ETendersOcds, Now);
        tender.DeriveStatus(Now);
        db.Tenders.Add(tender);
        await db.SaveChangesAsync();
        return (tender.Id, ocid);
    }

    [Fact]
    public async Task Listing_tenders_requires_authentication()
    {
        using var client = factory.CreateClient();

        (await client.GetAsync("/api/v1/tenders")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_viewer_can_list_and_search_tenders()
    {
        var (id, ocid) = await InsertTenderAsync("Request for quotation for SAP technical and functional support and maintenance services");
        using var client = await factory.CreateViewerClientAsync();

        var all = await client.GetFromJsonAsync<PagedResult<TenderListItemDto>>("/api/v1/tenders?pageSize=200");
        all!.Items.Should().Contain(t => t.Id == id && t.BuyerName == "ESKOM" && t.DocumentCount == 1 && t.Status == TenderStatus.Open);

        var searched = await client.GetFromJsonAsync<PagedResult<TenderListItemDto>>("/api/v1/tenders?q=sap%20functional%20support&pageSize=50");
        searched!.Items.Should().Contain(t => t.Ocid == ocid);
        searched.TotalCount.Should().BeGreaterThanOrEqualTo(1);

        var none = await client.GetFromJsonAsync<PagedResult<TenderListItemDto>>("/api/v1/tenders?q=zzzz-no-such-term-zzzz");
        none!.Items.Should().BeEmpty();
        none.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Filters_and_paging_are_validated()
    {
        using var client = await factory.CreateViewerClientAsync();

        (await client.GetAsync("/api/v1/tenders?pageSize=500")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/v1/tenders?page=0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/v1/tenders?status=Open&province=National&sort=NewestFirst")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Tender_detail_returns_documents_buyer_and_history()
    {
        var (id, ocid) = await InsertTenderAsync("Appointment of a service provider for enhancements, support and maintenance of the website");
        using var client = await factory.CreateViewerClientAsync();

        var detail = await client.GetFromJsonAsync<TenderDetailDto>($"/api/v1/tenders/{id}");

        detail!.Ocid.Should().Be(ocid);
        detail.BuyerName.Should().Be("ESKOM");
        detail.BuyerType.Should().Be(BuyerType.StateOwnedEntity);
        detail.Documents.Should().ContainSingle(d => d.Format == "pdf" && d.Status == DocumentStatus.Pending);
        detail.StatusHistory.Should().ContainSingle(h => h.To == TenderStatus.Open);
        detail.Classification.Should().BeNull();
        detail.SourceUrl.Should().StartWith("https://www.etenders.gov.za/");
    }

    [Fact]
    public async Task Unknown_tender_returns_404()
    {
        using var client = await factory.CreateViewerClientAsync();

        (await client.GetAsync($"/api/v1/tenders/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
