using MediatR;
using TenderSync.Api.Security;
using TenderSync.Application.Features.Classification;
using TenderSync.Application.Features.Classification.Queries.TestInterestCategories;

namespace TenderSync.Api.Endpoints;

/// <summary>Interest categories (PLAN.md §10). CRUD arrives with the dashboard work; WP07 ships the keyword dry run.</summary>
public static class InterestCategoryEndpoints
{
    public static IEndpointRouteBuilder MapInterestCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/interest-categories")
            .WithTags("Interest categories")
            .RequireAuthorization(Policies.Admin);

        group.MapPost("/test", async (TestInterestCategoriesQuery request, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(request, ct)))
            .WithSummary("Dry-runs the keyword prefilter against the enabled categories for a stored tender (tenderId) or free text.")
            .Produces<PrefilterResult>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
