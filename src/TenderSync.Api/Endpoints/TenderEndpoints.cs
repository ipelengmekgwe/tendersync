using MediatR;
using TenderSync.Api.Security;
using TenderSync.Application.Common.Models;
using TenderSync.Application.Features.Tenders;
using TenderSync.Application.Features.Tenders.Queries.GetTender;
using TenderSync.Application.Features.Tenders.Queries.ListTenders;
using TenderSync.Domain.Enums;

namespace TenderSync.Api.Endpoints;

public static class TenderEndpoints
{
    /// <summary>Query-string filters for GET /api/v1/tenders.</summary>
    public sealed class ListTendersRequest
    {
        /// <summary>Full-text search over bid number, title and description.</summary>
        public string? Q { get; init; }

        public TenderStatus? Status { get; init; }

        public Province? Province { get; init; }

        public Guid? BuyerId { get; init; }

        public DateTimeOffset? ClosingFrom { get; init; }

        public DateTimeOffset? ClosingTo { get; init; }

        /// <summary>Optional value types must be nullable here: minimal-API binding treats non-nullable ones as required.</summary>
        public bool? MatchedOnly { get; init; }

        public TenderSort? Sort { get; init; }

        public int? Page { get; init; }

        public int? PageSize { get; init; }

        public ListTendersQuery ToQuery() => new(
            Q, Status, Province, BuyerId, ClosingFrom, ClosingTo,
            MatchedOnly ?? false, Sort ?? TenderSort.ClosingSoonest, Page ?? 1, PageSize ?? Paging.DefaultPageSize);
    }

    public static IEndpointRouteBuilder MapTenderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tenders")
            .WithTags("Tenders")
            .RequireAuthorization(Policies.Viewer);

        group.MapGet("/", async ([AsParameters] ListTendersRequest request, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(request.ToQuery(), ct)))
            .WithSummary("Paged tender list with full-text search and filters.")
            .Produces<PagedResult<TenderListItemDto>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new GetTenderQuery(id), ct)))
            .WithSummary("Tender detail with buyer, documents, classification and status history.")
            .Produces<TenderDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
