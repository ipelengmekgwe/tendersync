using MediatR;
using TenderSync.Api.Security;
using TenderSync.Application.Common.Models;
using TenderSync.Application.Features.Matches;
using TenderSync.Application.Features.Matches.Commands.UpdateMatch;
using TenderSync.Application.Features.Matches.Queries.ListMatches;
using TenderSync.Application.Features.Overview.Queries.GetOverview;
using TenderSync.Domain.Enums;

namespace TenderSync.Api.Endpoints;

/// <summary>The matches inbox and the overview (PLAN.md §10, §11): GET /matches, PATCH /matches/{id}, GET /overview.</summary>
public static class MatchEndpoints
{
    /// <summary>Query-string filters for GET /api/v1/matches.</summary>
    public sealed class ListMatchesRequest
    {
        public MatchStatus? Status { get; init; }

        public Guid? TenderId { get; init; }

        public Guid? CompanyProfileId { get; init; }

        /// <summary>Only tenders that close between now and now + this many days.</summary>
        public int? ClosingWithinDays { get; init; }

        /// <summary>Only New, Reviewing and Applying.</summary>
        public bool? ActiveOnly { get; init; }

        public MatchSort? Sort { get; init; }

        public int? Page { get; init; }

        public int? PageSize { get; init; }

        public ListMatchesQuery ToQuery() => new(
            Status, TenderId, CompanyProfileId, ClosingWithinDays, ActiveOnly ?? false, Sort ?? MatchSort.ClosingSoonest, Page ?? 1, PageSize ?? Paging.DefaultPageSize);
    }

    /// <summary>PATCH body: null leaves a field as it is; an empty notes string clears the notes.</summary>
    public sealed record UpdateMatchRequest(MatchStatus? Status, string? Notes, string? DismissReason);

    public static IEndpointRouteBuilder MapMatchEndpoints(this IEndpointRouteBuilder app)
    {
        var matches = app.MapGroup("/api/v1/matches")
            .WithTags("Matches")
            .RequireAuthorization(Policies.Viewer);

        matches.MapGet("/", async ([AsParameters] ListMatchesRequest request, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(request.ToQuery(), ct)))
            .WithSummary("The matches inbox: matched tenders with workflow status, relevance, newest verdict and closing date, plus counts per status for the tabs.")
            .Produces<MatchInboxDto>()
            .ProducesValidationProblem();

        matches.MapPatch("/{id:guid}", async (Guid id, UpdateMatchRequest request, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new UpdateMatchCommand(id, request.Status, request.Notes, request.DismissReason), ct)))
            .RequireAuthorization(Policies.Reviewer)
            .WithSummary("Changes a match's status and notes. Dismissing needs a dismiss reason. Records a decision only; nothing is submitted (ADR-015). Reviewer or Admin.")
            .Produces<MatchDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/v1/overview", async (ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new GetOverviewQuery(), ct)))
            .WithTags("Overview")
            .RequireAuthorization(Policies.Viewer)
            .WithSummary("Overview page data: new matches, matched tenders closing soon, pipeline status, model cost this month (Admin only) and expiring company documents.")
            .Produces<OverviewDto>();

        return app;
    }
}
