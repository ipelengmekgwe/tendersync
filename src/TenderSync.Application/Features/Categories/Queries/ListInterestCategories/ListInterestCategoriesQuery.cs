using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;

namespace TenderSync.Application.Features.Categories.Queries.ListInterestCategories;

/// <summary>GET /interest-categories (Admin): every category, enabled or not, ordered by code.</summary>
[Authorize(Roles.Admin)]
public sealed record ListInterestCategoriesQuery : IQuery<IReadOnlyList<InterestCategoryDto>>;

public sealed class ListInterestCategoriesQueryHandler(IAppDbContext db) : IRequestHandler<ListInterestCategoriesQuery, IReadOnlyList<InterestCategoryDto>>
{
    public async Task<IReadOnlyList<InterestCategoryDto>> Handle(ListInterestCategoriesQuery request, CancellationToken cancellationToken)
    {
        var categories = await db.InterestCategories.ListAsync(enabledOnly: false, cancellationToken);
        return categories.OrderBy(c => c.Code, StringComparer.Ordinal).Select(InterestCategoryDto.From).ToList();
    }
}
