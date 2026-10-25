using FluentValidation;
using MediatR;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Common.Models;
using TenderSync.Application.Security;
using TenderSync.Domain.Enums;

namespace TenderSync.Application.Features.Matches.Queries.ListMatches;

public enum MatchSort
{
    /// <summary>Open tenders first, closing soonest at the top; closed ones after them.</summary>
    ClosingSoonest = 0,
    MostRelevant = 1,
    NewestFirst = 2,
}

/// <summary>
/// GET /matches (PLAN.md §10): the inbox. Filter by workflow status (the tabs), by tender (the detail page's match
/// control), by company profile, or to tenders closing within N days (the "closing soon" view). ActiveOnly keeps the
/// statuses someone still has to act on: New, Reviewing and Applying.
/// </summary>
[Authorize(Roles.Viewer)]
public sealed record ListMatchesQuery(
    MatchStatus? Status = null,
    Guid? TenderId = null,
    Guid? CompanyProfileId = null,
    int? ClosingWithinDays = null,
    bool ActiveOnly = false,
    MatchSort Sort = MatchSort.ClosingSoonest,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize) : IQuery<MatchInboxDto>;

public sealed class ListMatchesQueryValidator : AbstractValidator<ListMatchesQuery>
{
    public ListMatchesQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
        RuleFor(q => q.ClosingWithinDays).InclusiveBetween(1, 365).When(q => q.ClosingWithinDays is not null);
        RuleFor(q => q.Status).IsInEnum().When(q => q.Status is not null);
    }
}

public sealed class ListMatchesQueryHandler(IReadDb db, IDateTimeProvider clock) : IRequestHandler<ListMatchesQuery, MatchInboxDto>
{
    private const string Filters = """
        (@tenderId::uuid is null or m.tender_id = @tenderId)
          and (@profileId::uuid is null or m.company_profile_id = @profileId)
          and (@closingBefore::timestamptz is null or (t.closing_at >= @now and t.closing_at < @closingBefore))
          and (@activeOnly = false or m.status = any(@activeStatuses))
        """;

    private static readonly int[] ActiveStatuses = [(int)MatchStatus.New, (int)MatchStatus.Reviewing, (int)MatchStatus.Applying];

    private const string ItemsSql = """
        select
            m.id,
            m.tender_id,
            m.company_profile_id,
            p.name as company_name,
            t.bid_number,
            t.title,
            b.name as buyer_name,
            t.province,
            t.closing_at,
            t.status as tender_status,
            t.briefing_at,
            t.briefing_compulsory,
            m.relevance,
            m.status,
            m.notes,
            m.dismiss_reason,
            m.created_at,
            m.updated_at,
            q.verdict,
            q.score,
            q.created_at as qualified_at,
            count(*) over() as total_count
        from tendersync.tender_matches m
        join tendersync.tenders t on t.id = m.tender_id
        join tendersync.buyers b on b.id = t.buyer_id
        join tendersync.company_profiles p on p.id = m.company_profile_id
        left join lateral (
            select r.verdict, r.score, r.created_at
            from tendersync.qualification_reports r
            where r.tender_id = m.tender_id and r.company_profile_id = m.company_profile_id
            order by r.created_at desc, r.id desc
            limit 1) q on true
        where (@status::int is null or m.status = @status)
          and {filters}
        order by {order}
        limit @pageSize offset @offset
        """;

    private const string CountsSql = """
        select m.status, count(*)::int as count
        from tendersync.tender_matches m
        join tendersync.tenders t on t.id = m.tender_id
        where {filters}
        group by m.status
        """;

    public async Task<MatchInboxDto> Handle(ListMatchesQuery request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var orderBy = request.Sort switch
        {
            MatchSort.MostRelevant => "m.relevance desc, t.closing_at asc nulls last, m.id",
            MatchSort.NewestFirst => "m.created_at desc, m.id",
            _ => "case when t.closing_at >= @now then 0 when t.closing_at is null then 1 else 2 end, t.closing_at asc, m.relevance desc, m.id",
        };

        var args = new
        {
            status = (int?)request.Status,
            tenderId = request.TenderId,
            profileId = request.CompanyProfileId,
            now,
            closingBefore = request.ClosingWithinDays is { } days ? now.AddDays(days) : (DateTimeOffset?)null,
            activeOnly = request.ActiveOnly,
            activeStatuses = ActiveStatuses,
            pageSize = request.PageSize,
            offset = Paging.Offset(request.Page, request.PageSize),
        };

        var rows = await db.QueryAsync<MatchRow>(ItemsSql.Replace("{filters}", Filters, StringComparison.Ordinal).Replace("{order}", orderBy, StringComparison.Ordinal), args, cancellationToken);
        var counts = await db.QueryAsync<CountRow>(CountsSql.Replace("{filters}", Filters, StringComparison.Ordinal), args, cancellationToken);

        var items = rows.Select(r => new MatchListItemDto(
            r.Id, r.TenderId, r.CompanyProfileId, r.CompanyName, r.BidNumber, r.Title, r.BuyerName, (Province)r.Province,
            r.ClosingAt, (TenderStatus)r.TenderStatus, r.BriefingAt, r.BriefingCompulsory, r.Relevance, (MatchStatus)r.Status,
            r.Notes, r.DismissReason, r.CreatedAt, r.UpdatedAt,
            r.Verdict is null ? null : (QualificationVerdict)r.Verdict.Value, r.Score, r.QualifiedAt)).ToList();

        var byStatus = counts.ToDictionary(c => (MatchStatus)c.Status, c => c.Count);
        var allCounts = Enum.GetValues<MatchStatus>().Select(s => new MatchStatusCountDto(s, byStatus.GetValueOrDefault(s))).ToList();

        return new MatchInboxDto(items, request.Page, request.PageSize, rows.Count == 0 ? 0 : (int)rows[0].TotalCount, allCounts);
    }

    private sealed class MatchRow
    {
        public Guid Id { get; init; }

        public Guid TenderId { get; init; }

        public Guid CompanyProfileId { get; init; }

        public string CompanyName { get; init; } = string.Empty;

        public string BidNumber { get; init; } = string.Empty;

        public string Title { get; init; } = string.Empty;

        public string BuyerName { get; init; } = string.Empty;

        public int Province { get; init; }

        public DateTimeOffset? ClosingAt { get; init; }

        public int TenderStatus { get; init; }

        public DateTimeOffset? BriefingAt { get; init; }

        public bool? BriefingCompulsory { get; init; }

        public int Relevance { get; init; }

        public int Status { get; init; }

        public string? Notes { get; init; }

        public string? DismissReason { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public int? Verdict { get; init; }

        public int? Score { get; init; }

        public DateTimeOffset? QualifiedAt { get; init; }

        public long TotalCount { get; init; }
    }

    private sealed class CountRow
    {
        public int Status { get; init; }

        public int Count { get; init; }
    }
}
