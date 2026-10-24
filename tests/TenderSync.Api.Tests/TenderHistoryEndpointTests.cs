using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Features.Status.Queries.GetTenderHistory;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Notifications;
using TenderSync.Domain.Tenders;

namespace TenderSync.Api.Tests;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class TenderHistoryEndpointTests(ApiFactory factory)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task History_lists_status_transitions_releases_and_notifications_with_masked_recipients()
    {
        var ocid = $"ocds-9t57fa-{Random.Shared.Next(100000, 999999)}";
        Guid tenderId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var buyer = await db.Buyers.GetByExternalIdAsync("167");
            if (buyer is null)
            {
                buyer = Buyer.Create("167", "ESKOM", BuyerType.StateOwnedEntity, Province.National);
                db.Buyers.Add(buyer);
            }

            TenderSnapshot Snapshot(DateTimeOffset closing, string release) => new(
                ocid, ocid[^6..], $"E{ocid[^6..]}", "SAP support", "SAP support services", "167", "ESKOM",
                ProcurementCategory.Services, ["services"], "open", "Request for Quotation", null, "ZAR", Now, closing,
                $"https://www.etenders.gov.za/Home/TenderDetails?Id={ocid[^6..]}", release, Now, "compiled", "{}",
                [new TenderDocumentSnapshot($"{ocid}-doc", "Bid Document.pdf", "https://www.etenders.gov.za/home/Download?blobName=x.pdf", "pdf", "en", Now, Now)], false);

            var tender = Tender.CreateFromSnapshot(Snapshot(Now.AddDays(21), $"{ocid}-r1"), buyer.Id, Province.National, TenderSourceKind.ETendersOcds, Now);
            tender.DeriveStatus(Now);
            tender.ApplyRelease(Snapshot(Now.AddDays(3), $"{ocid}-r2"), Province.National, Now.AddHours(1));
            tender.DeriveStatus(Now.AddHours(1));
            db.Tenders.Add(tender);
            db.Notifications.Add(Notification.Create(NotificationType.TenderChanged, NotificationChannel.Email, "jane@example.com", "Tender changed", "body", tender.Id, null, "abc"));
            await db.SaveChangesAsync();
            tenderId = tender.Id;
        }

        using var viewer = await factory.CreateViewerClientAsync();
        var history = await viewer.GetFromJsonAsync<TenderHistoryDto>($"/api/v1/tenders/{tenderId}/history");

        history!.Status.Should().Be(TenderStatus.ClosingSoon);
        history.StatusHistory.Select(h => h.To).Should().Equal(TenderStatus.Open, TenderStatus.ClosingSoon);
        history.Releases.Select(r => r.ReleaseId).Should().Equal($"{ocid}-r1", $"{ocid}-r2");
        history.Notifications.Should().ContainSingle().Which.Should().Match<TenderNotificationDto>(n => n.Type == NotificationType.TenderChanged && n.Recipient == "ja***@example.com");
        (await viewer.GetAsync($"/api/v1/tenders/{Guid.CreateVersion7()}/history")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
