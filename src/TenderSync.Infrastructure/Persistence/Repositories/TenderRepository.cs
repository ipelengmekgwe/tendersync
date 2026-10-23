using Microsoft.EntityFrameworkCore;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Domain.Tenders;

namespace TenderSync.Infrastructure.Persistence.Repositories;

internal sealed class TenderRepository(AppDbContext context) : EfRepository<Tender>(context), ITenderRepository
{
    private IQueryable<Tender> WithDetails() =>
        Set.Include(t => t.Buyer)
           .Include(t => t.Documents)
           .Include(t => t.Releases)
           .Include(t => t.StatusHistory);

    public Task<Tender?> GetByOcidAsync(string ocid, CancellationToken cancellationToken = default) =>
        WithDetails().FirstOrDefaultAsync(t => t.Ocid == ocid, cancellationToken);

    public Task<Tender?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default) =>
        WithDetails().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyDictionary<string, Guid>> GetIdsByOcidsAsync(IEnumerable<string> ocids, CancellationToken cancellationToken = default)
    {
        var list = ocids.Distinct(StringComparer.Ordinal).ToArray();
        if (list.Length == 0)
        {
            return new Dictionary<string, Guid>(StringComparer.Ordinal);
        }

        return await Set.Where(t => list.Contains(t.Ocid))
            .Select(t => new { t.Ocid, t.Id })
            .ToDictionaryAsync(x => x.Ocid, x => x.Id, StringComparer.Ordinal, cancellationToken);
    }

    public Task<TenderDocument?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        Context.TenderDocumentSet.FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

    public Task<TenderClassification?> GetClassificationAsync(Guid tenderId, CancellationToken cancellationToken = default) =>
        Context.TenderClassificationSet.FirstOrDefaultAsync(c => c.TenderId == tenderId, cancellationToken);

    public void AddClassification(TenderClassification classification) => Context.TenderClassificationSet.Add(classification);

    public async Task ReplaceDocumentTextsAsync(Guid documentId, IEnumerable<DocumentText> pages, CancellationToken cancellationToken = default)
    {
        await Context.DocumentTextSet.Where(t => t.DocumentId == documentId).ExecuteDeleteAsync(cancellationToken);

        // ExecuteDelete bypasses the change tracker; drop any instances it still holds for this document so the new pages can be added.
        foreach (var entry in Context.ChangeTracker.Entries<DocumentText>().Where(e => e.Entity.DocumentId == documentId).ToList())
        {
            entry.State = EntityState.Detached;
        }

        Context.DocumentTextSet.AddRange(pages);
    }

    public async Task<IReadOnlyList<DocumentText>> GetDocumentTextsAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        await Context.DocumentTextSet.Where(t => t.DocumentId == documentId).OrderBy(t => t.Page).ToListAsync(cancellationToken);
}
