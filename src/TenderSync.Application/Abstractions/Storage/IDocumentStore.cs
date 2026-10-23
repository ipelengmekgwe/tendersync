namespace TenderSync.Application.Abstractions.Storage;

/// <summary>Binary storage for tender and company documents (filesystem locally, Azure Blob on Azure; ADR-013).</summary>
public interface IDocumentStore
{
    /// <summary>Writes the stream to the given relative path (e.g. tenders/{ocid}/{id}.pdf), overwriting. Returns the stored path.</summary>
    Task<string> SaveAsync(string path, Stream content, string? contentType, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);

    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
}
