using MediatR;
using TenderSync.Api.Security;
using TenderSync.Application.Common.Models;
using TenderSync.Application.Features.Classification;
using TenderSync.Application.Features.Classification.Commands.ReclassifyTender;
using TenderSync.Application.Features.Requirements.Commands.RequestExtraction;
using TenderSync.Application.Features.Requirements.Queries.GetTenderRequirements;
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

        /// <summary>Classified between the near-miss and match thresholds, without a match.</summary>
        public bool? NearMisses { get; init; }

        public int? MinRelevance { get; init; }

        public TenderSort? Sort { get; init; }

        public int? Page { get; init; }

        public int? PageSize { get; init; }

        public ListTendersQuery ToQuery() => new(
            Q, Status, Province, BuyerId, ClosingFrom, ClosingTo,
            MatchedOnly ?? false, NearMisses ?? false, MinRelevance, Sort ?? TenderSort.ClosingSoonest, Page ?? 1, PageSize ?? Paging.DefaultPageSize);
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

        group.MapPost("/{id:guid}/reclassify", async (Guid id, bool? prefilterOnly, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new ReclassifyTenderCommand(id, prefilterOnly ?? false), ct)))
            .RequireAuthorization(Policies.Reviewer)
            .WithSummary("Runs the keyword prefilter and the model classification again and returns the outcome. Reviewer or Admin.")
            .Produces<ClassificationOutcome>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/requirements", async (Guid id, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new GetTenderRequirementsQuery(id), ct)))
            .WithSummary("Extracted requirements grouped by kind, each with its source document, page, quote and verify flag.")
            .Produces<TenderRequirementsDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/extract", async (Guid id, ISender sender, CancellationToken ct) =>
                TypedResults.Accepted((string?)null, await sender.Send(new RequestExtractionCommand(id), ct)))
            .RequireAuthorization(Policies.Reviewer)
            .WithSummary("Queues requirement extraction (and any outstanding document downloads first). Reviewer or Admin.")
            .Produces<RequestExtractionResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
