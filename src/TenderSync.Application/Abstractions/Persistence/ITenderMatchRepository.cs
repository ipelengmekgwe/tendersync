using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Abstractions.Persistence;

public interface ITenderMatchRepository : IRepository<TenderMatch>
{
    Task<TenderMatch?> FindAsync(Guid tenderId, Guid companyProfileId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenderMatch>> ListByTenderAsync(Guid tenderId, CancellationToken cancellationToken = default);
}
