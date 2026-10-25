using MediatR;
using TenderSync.Api.Security;
using TenderSync.Application.Features.Applications.Queries.ListApplications;

namespace TenderSync.Api.Endpoints;

/// <summary>Bid applications (PLAN.md §10). v1 lists them; Phase 2 adds create, generate and package.</summary>
public static class ApplicationEndpoints
{
    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/v1/applications")
            .WithTags("Applications")
            .RequireAuthorization(Policies.Reviewer)
            .MapGet("/", async (ISender sender, CancellationToken ct) => TypedResults.Ok(await sender.Send(new ListApplicationsQuery(), ct)))
            .WithSummary("Bid applications, newest first. Empty in v1: applications are created from Phase 2.")
            .Produces<IReadOnlyList<ApplicationListItemDto>>();

        return app;
    }
}
