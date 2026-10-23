using TenderSync.Domain.Geography;

namespace TenderSync.Application.Abstractions.Persistence;

public interface IMunicipalityRepository : IRepository<Municipality>
{
    Task<IReadOnlyList<Municipality>> ListAsync(CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);
}
