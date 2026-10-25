using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Matches;

/// <summary>One row of the matches inbox (PLAN.md §11 /matches): the tender, the match workflow state and the newest verdict.</summary>
public sealed record MatchListItemDto(
    Guid Id,
    Guid TenderId,
    Guid CompanyProfileId,
    string CompanyName,
    string BidNumber,
    string Title,
    string BuyerName,
    Province Province,
    DateTimeOffset? ClosingAt,
    TenderStatus TenderStatus,
    DateTimeOffset? BriefingAt,
    bool? BriefingCompulsory,
    int Relevance,
    MatchStatus Status,
    string? Notes,
    string? DismissReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    QualificationVerdict? Verdict,
    int? Score,
    DateTimeOffset? QualifiedAt);

public sealed record MatchStatusCountDto(MatchStatus Status, int Count);

/// <summary>A page of the inbox plus the count per status for the tabs (same filters, any status).</summary>
public sealed record MatchInboxDto(
    IReadOnlyList<MatchListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<MatchStatusCountDto> Counts);

/// <summary>The match after a change (PATCH /matches/{id}).</summary>
public sealed record MatchDto(
    Guid Id,
    Guid TenderId,
    Guid CompanyProfileId,
    int Relevance,
    MatchStatus Status,
    string? Notes,
    string? DismissReason,
    DateTimeOffset? DismissedAt,
    DateTimeOffset? NotifiedAt)
{
    public static MatchDto From(TenderMatch match) => new(
        match.Id, match.TenderId, match.CompanyProfileId, match.Relevance, match.Status, match.Notes, match.DismissReason, match.DismissedAt, match.NotifiedAt);
}
