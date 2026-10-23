using FluentValidation;
using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Common.Models;
using TenderSync.Application.Security;
using TenderSync.Domain.Enums;

namespace TenderSync.Application.Features.Tenders.Queries.ListTenders;

public enum TenderSort
{
    ClosingSoonest = 0,
    NewestFirst = 1,
    MostRelevant = 2,
}

/// <summary>Paged tender list with full-text search (tenders.search_vector) and the filters from PLAN.md §10.</summary>
[Authorize(Roles.Viewer)]
public sealed record ListTendersQuery(
    string? Q = null,
    TenderStatus? Status = null,
    Province? Province = null,
    Guid? BuyerId = null,
    DateTimeOffset? ClosingFrom = null,
    DateTimeOffset? ClosingTo = null,
    bool MatchedOnly = false,
    TenderSort Sort = TenderSort.ClosingSoonest,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize) : IQuery<PagedResult<TenderListItemDto>>;

public sealed class ListTendersQueryValidator : AbstractValidator<ListTendersQuery>
{
    public ListTendersQueryValidator()
    {
        RuleFor(q => q.Q).MaximumLength(200);
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
        RuleFor(q => q).Must(q => q.ClosingFrom is null || q.ClosingTo is null || q.ClosingFrom <= q.ClosingTo)
            .WithMessage("closingFrom must be on or before closingTo.");
    }
}

public sealed class ListTendersQueryHandler(IReadDb db) : IRequestHandler<ListTendersQuery, PagedResult<TenderListItemDto>>
{
    private const string BaseSql = """
        select
            t.id,
            t.ocid,
            t.bid_number,
            t.title,
            b.name as buyer_name,
            t.province,
            t.main_category,
            t.procurement_method_details,
            t.published_at,
            t.closing_at,
            t.status,
            c.relevance,
            (select m.status from tendersync.tender_matches m where m.tender_id = t.id order by m.created_at desc limit 1) as match_status,
            (select count(*) from tendersync.tender_documents d where d.tender_id = t.id)::int as document_count,
            count(*) over() as total_count
        from tendersync.tenders t
        join tendersync.buyers b on b.id = t.buyer_id
        left join tendersync.tender_classifications c on c.tender_id = t.id
        where (@q::text is null or t.search_vector @@ plainto_tsquery('english', @q))
          and (@status::int is null or t.status = @status)
          and (@province::int is null or t.province = @province)
          and (@buyerId::uuid is null or t.buyer_id = @buyerId)
          and (@closingFrom::timestamptz is null or t.closing_at >= @closingFrom)
          and (@closingTo::timestamptz is null or t.closing_at <= @closingTo)
          and (@matchedOnly = false or exists (select 1 from tendersync.tender_matches m where m.tender_id = t.id))
        """;

    public async Task<PagedResult<TenderListItemDto>> Handle(ListTendersQuery request, CancellationToken cancellationToken)
    {
        var orderBy = request.Sort switch
        {
            TenderSort.NewestFirst => "t.published_at desc, t.id",
            TenderSort.MostRelevant => "c.relevance desc nulls last, t.closing_at asc nulls last, t.id",
            _ => "t.closing_at asc nulls last, t.id",
        };

        var sql = $"{BaseSql}\norder by {orderBy}\nlimit @pageSize offset @offset";

        var rows = await db.QueryAsync<TenderListRow>(sql, new
        {
            q = string.IsNullOrWhiteSpace(request.Q) ? null : request.Q.Trim(),
            status = (int?)request.Status,
            province = (int?)request.Province,
            buyerId = request.BuyerId,
            closingFrom = request.ClosingFrom,
            closingTo = request.ClosingTo,
            matchedOnly = request.MatchedOnly,
            pageSize = request.PageSize,
            offset = Paging.Offset(request.Page, request.PageSize),
        }, cancellationToken);

        if (rows.Count == 0)
        {
            return PagedResult.Empty<TenderListItemDto>(request.Page, request.PageSize);
        }

        var items = rows.Select(r => new TenderListItemDto(
            r.Id, r.Ocid, r.BidNumber, r.Title, r.BuyerName, (Province)r.Province, (ProcurementCategory)r.MainCategory,
            r.ProcurementMethodDetails, r.PublishedAt, r.ClosingAt, (TenderStatus)r.Status, r.Relevance,
            r.MatchStatus is null ? null : (MatchStatus)r.MatchStatus.Value, r.DocumentCount)).ToList();

        return new PagedResult<TenderListItemDto>(items, request.Page, request.PageSize, (int)rows[0].TotalCount);
    }

    private sealed class TenderListRow
    {
        public Guid Id { get; init; }

        public string Ocid { get; init; } = string.Empty;

        public string BidNumber { get; init; } = string.Empty;

        public string Title { get; init; } = string.Empty;

        public string BuyerName { get; init; } = string.Empty;

        public int Province { get; init; }

        public int MainCategory { get; init; }

        public string? ProcurementMethodDetails { get; init; }

        public DateTimeOffset PublishedAt { get; init; }

        public DateTimeOffset? ClosingAt { get; init; }

        public int Status { get; init; }

        public int? Relevance { get; init; }

        public int? MatchStatus { get; init; }

        public int DocumentCount { get; init; }

        public long TotalCount { get; init; }
    }
}
