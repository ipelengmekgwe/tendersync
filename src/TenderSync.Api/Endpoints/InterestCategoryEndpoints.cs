using MediatR;
using TenderSync.Api.Security;
using TenderSync.Application.Features.Categories;
using TenderSync.Application.Features.Categories.Commands.CreateInterestCategory;
using TenderSync.Application.Features.Categories.Commands.DeleteInterestCategory;
using TenderSync.Application.Features.Categories.Commands.UpdateInterestCategory;
using TenderSync.Application.Features.Categories.Queries.ListInterestCategories;
using TenderSync.Application.Features.Classification;
using TenderSync.Application.Features.Classification.Queries.TestInterestCategories;

namespace TenderSync.Api.Endpoints;

/// <summary>Interest categories (PLAN.md §10): list, create, update, delete, and the keyword dry run. Admin only.</summary>
public static class InterestCategoryEndpoints
{
    /// <summary>POST body: the code plus the editable fields.</summary>
    public sealed record CreateRequest(
        string Code,
        string Name,
        string? Description,
        IReadOnlyList<string> Keywords,
        IReadOnlyList<string> NegativeKeywords,
        int Weight,
        bool Enabled)
    {
        public CreateInterestCategoryCommand ToCommand() => new(Code, new InterestCategoryInput(Name, Description, Keywords, NegativeKeywords, Weight, Enabled));
    }

    public static IEndpointRouteBuilder MapInterestCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/interest-categories")
            .WithTags("Interest categories")
            .RequireAuthorization(Policies.Admin);

        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new ListInterestCategoriesQuery(), ct)))
            .WithSummary("Every interest category, enabled or not, ordered by code.")
            .Produces<IReadOnlyList<InterestCategoryDto>>();

        group.MapPost("/", async (CreateRequest request, ISender sender, CancellationToken ct) =>
            {
                var created = await sender.Send(request.ToCommand(), ct);
                return TypedResults.Created($"/api/v1/interest-categories/{created.Id}", created);
            })
            .WithSummary("Creates a category. Codes are unique (409 when taken).")
            .Produces<InterestCategoryDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", async (Guid id, InterestCategoryInput input, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new UpdateInterestCategoryCommand(id, input), ct)))
            .WithSummary("Replaces name, description, keywords, negative keywords, weight and enabled. Reclassify a tender to see the effect.")
            .Produces<InterestCategoryDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new DeleteInterestCategoryCommand(id), ct);
                return TypedResults.NoContent();
            })
            .WithSummary("Deletes a category. Disabling it is the reversible alternative.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/test", async (TestInterestCategoriesQuery request, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(request, ct)))
            .WithSummary("Dry-runs the keyword prefilter against the enabled categories for a stored tender (tenderId) or free text.")
            .Produces<PrefilterResult>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
