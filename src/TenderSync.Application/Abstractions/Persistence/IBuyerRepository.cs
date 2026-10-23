using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Abstractions.Persistence;

public interface IBuyerRepository : IRepository<Buyer>
{
    Task<Buyer?> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, Buyer>> GetByExternalIdsAsync(IEnumerable<string> externalIds, CancellationToken cancellationToken = default);
}
