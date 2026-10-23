using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Abstractions.Persistence;

public interface ITenderRepository : IRepository<Tender>
{
    /// <summary>Loads a tender with its documents, releases and status history for mutation.</summary>
    Task<Tender?> GetByOcidAsync(string ocid, CancellationToken cancellationToken = default);

    /// <summary>Loads a tender with its documents, releases and status history for mutation.</summary>
    Task<Tender?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, Guid>> GetIdsByOcidsAsync(IEnumerable<string> ocids, CancellationToken cancellationToken = default);

    Task<TenderDocument?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);

    Task<TenderClassification?> GetClassificationAsync(Guid tenderId, CancellationToken cancellationToken = default);

    void AddClassification(TenderClassification classification);

    Task ReplaceDocumentTextsAsync(Guid documentId, IEnumerable<DocumentText> pages, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentText>> GetDocumentTextsAsync(Guid documentId, CancellationToken cancellationToken = default);
}
