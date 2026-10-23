using Microsoft.EntityFrameworkCore;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Domain.Common;

namespace TenderSync.Infrastructure.Persistence.Repositories;

internal class EfRepository<TEntity>(AppDbContext context) : IRepository<TEntity>
    where TEntity : Entity
{
    protected AppDbContext Context { get; } = context;

    protected DbSet<TEntity> Set => Context.Set<TEntity>();

    public virtual Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Set.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public virtual async Task<IReadOnlyList<TEntity>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().ToArray();
        return idList.Length == 0 ? [] : await Set.Where(e => idList.Contains(e.Id)).ToListAsync(cancellationToken);
    }

    public void Add(TEntity entity) => Set.Add(entity);

    public void AddRange(IEnumerable<TEntity> entities) => Set.AddRange(entities);

    public void Remove(TEntity entity) => Set.Remove(entity);
}
