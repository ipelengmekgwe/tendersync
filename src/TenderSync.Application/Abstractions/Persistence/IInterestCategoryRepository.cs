using TenderSync.Domain.Categories;

namespace TenderSync.Application.Abstractions.Persistence;

public interface IInterestCategoryRepository : IRepository<InterestCategory>
{
    Task<IReadOnlyList<InterestCategory>> ListAsync(bool enabledOnly, CancellationToken cancellationToken = default);

    Task<InterestCategory?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
}
