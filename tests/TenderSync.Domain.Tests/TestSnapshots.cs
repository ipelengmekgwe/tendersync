using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Domain.Tests;

/// <summary>Builders for tender snapshots modelled on real eTenders releases (docs/research/etenders-samples).</summary>
public static class TestSnapshots
{
    public static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    public static TenderDocumentSnapshot Document(string id = "ce8062bc-9ea9-494f-84b5-e92d1730bbb0", string title = "Bid Document WCGHCC0038-1-2025 - Comfort Packs.pdf", DateTimeOffset? modified = null) =>
        new(id, title, $"https://www.etenders.gov.za/home/Download?blobName={id}.pdf&downloadedFileName={Uri.EscapeDataString(title)}", "pdf", "en", Now.AddHours(-1), modified ?? Now.AddHours(-1));

    public static readonly DateTimeOffset DefaultClosing = new(2026, 11, 20, 11, 0, 0, TimeSpan.Zero);

    public static TenderSnapshot ComfortPacks(
        DateTimeOffset? closingAt = null,
        bool noClosing = false,
        string? description = null,
        IReadOnlyList<TenderDocumentSnapshot>? documents = null,
        string releaseId = "ocds-9t57fa-175445-2026/10/23 00:00:00",
        bool hasAwards = false) =>
        new(
            Ocid: "ocds-9t57fa-175445",
            ExternalTenderId: "175445",
            BidNumber: "WCGHCC0038/1/2025",
            Title: "Supplementary bid for the supply and delivery of comfort packs",
            Description: description ?? "SUPPLEMENTARY BID FOR THE SUPPLY AND DELIVERY OF COMFORT PACKS TO DISTRICT & SUB-STRUCTURE OFFICES UNDER THE CONTROL OF THE WESTERN CAPE GOVERNMENT HEALTH AND WELLNESS FOR A PERIOD ENDING 30 SEPTEMBER 2028",
            BuyerExternalId: "723",
            BuyerName: "Western Cape - Health",
            MainCategory: ProcurementCategory.Goods,
            AdditionalCategories: ["goods"],
            ProcurementMethod: "open",
            ProcurementMethodDetails: "Request for Bid(Open-Tender)",
            ValueAmount: 0,
            ValueCurrency: "ZAR",
            PublishedAt: Now.Date,
            ClosingAt: noClosing ? null : closingAt ?? DefaultClosing,
            SourceUrl: "https://www.etenders.gov.za/Home/TenderDetails?Id=175445",
            ReleaseId: releaseId,
            ReleaseDate: Now.Date,
            ReleaseTag: "compiled",
            RawJson: """{"ocid":"ocds-9t57fa-175445","tag":["compiled"]}""",
            Documents: documents ?? [Document()],
            HasAwards: hasAwards);
}
