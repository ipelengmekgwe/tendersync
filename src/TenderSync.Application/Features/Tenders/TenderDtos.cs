using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Tenders;

public sealed record TenderListItemDto(
    Guid Id,
    string Ocid,
    string BidNumber,
    string Title,
    string BuyerName,
    Province Province,
    ProcurementCategory MainCategory,
    string? ProcurementMethodDetails,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ClosingAt,
    TenderStatus Status,
    int? Relevance,
    MatchStatus? MatchStatus,
    int DocumentCount);

public sealed record TenderDocumentDto(
    Guid Id,
    string Title,
    string Url,
    string? Format,
    DocumentStatus Status,
    int? PageCount,
    bool NeedsOcr,
    DateTimeOffset? DatePublished,
    DateTimeOffset? DateModified,
    long? SizeBytes,
    string? Sha256,
    DateTimeOffset? DownloadedAt,
    DateTimeOffset? ExtractedAt,
    string? Error)
{
    /// <summary>True when the API can stream a stored copy (GET .../documents/{docId}/download).</summary>
    public bool Stored => DownloadedAt is not null;

    public static TenderDocumentDto From(TenderDocument d) => new(
        d.Id, d.Title, d.Url, d.Format, d.Status, d.PageCount, d.NeedsOcr, d.DatePublished, d.DateModified,
        d.SizeBytes, d.Sha256, d.DownloadedAt, d.ExtractedAt, d.Error);
}

public sealed record TenderClassificationDto(ClassificationStage Stage, IReadOnlyList<string> CategoryCodes, int Relevance, string? Rationale, DateTimeOffset ClassifiedAt);

public sealed record TenderStatusHistoryDto(TenderStatus From, TenderStatus To, string? Reason, DateTimeOffset At);

public sealed record TenderDetailDto(
    Guid Id,
    string Ocid,
    string BidNumber,
    string Title,
    string Description,
    Guid BuyerId,
    string BuyerName,
    BuyerType BuyerType,
    Province Province,
    ProcurementCategory MainCategory,
    IReadOnlyList<string> AdditionalCategories,
    string? ProcurementMethod,
    string? ProcurementMethodDetails,
    decimal? ValueAmount,
    string? ValueCurrency,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ClosingAt,
    DateTimeOffset? BriefingAt,
    bool? BriefingCompulsory,
    string? BriefingVenue,
    SubmissionMethod SubmissionMethod,
    TenderStatus Status,
    string? StatusReason,
    string SourceUrl,
    bool HasAwards,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    IReadOnlyList<TenderDocumentDto> Documents,
    TenderClassificationDto? Classification,
    IReadOnlyList<TenderStatusHistoryDto> StatusHistory);
