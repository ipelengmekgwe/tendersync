using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Tenders;

/// <summary>
/// Source-neutral view of one published release of a tender. Infrastructure maps OCDS (and later other portals)
/// into this shape; <see cref="Tender.ApplyRelease"/> consumes it. Keeps the Domain free of OCDS details.
/// </summary>
public sealed record TenderSnapshot(
    string Ocid,
    string ExternalTenderId,
    string BidNumber,
    string Title,
    string Description,
    string BuyerExternalId,
    string BuyerName,
    ProcurementCategory MainCategory,
    IReadOnlyList<string> AdditionalCategories,
    string? ProcurementMethod,
    string? ProcurementMethodDetails,
    decimal? ValueAmount,
    string? ValueCurrency,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ClosingAt,
    string SourceUrl,
    string ReleaseId,
    DateTimeOffset ReleaseDate,
    string ReleaseTag,
    string RawJson,
    IReadOnlyList<TenderDocumentSnapshot> Documents,
    bool HasAwards);
