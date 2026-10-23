using TenderSync.Domain.Common;

namespace TenderSync.Application.Abstractions.Persistence;

/// <summary>Minimal write-side access to an aggregate. Read-heavy queries go through <see cref="IReadDb"/>.</summary>
public interface IRepository<TEntity>
    where TEntity : Entity
{
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TEntity>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    void Add(TEntity entity);

    void AddRange(IEnumerable<TEntity> entities);

    void Remove(TEntity entity);
}
