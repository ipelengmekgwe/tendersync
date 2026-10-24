using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using TenderSync.Application.Abstractions.Storage;

namespace TenderSync.Infrastructure.Storage;

/// <summary>One container, blob name = store path. The container is created on first use so a fresh storage account works without setup.</summary>
public sealed class AzureBlobDocumentStore(BlobContainerClient container) : IDocumentStore, IDisposable
{
    private readonly SemaphoreSlim _init = new(1, 1);
    private bool _ready;

    public void Dispose() => _init.Dispose();

    public async Task<string> SaveAsync(string path, Stream content, string? contentType, CancellationToken cancellationToken = default)
    {
        var blob = await BlobAsync(path, cancellationToken);
        await blob.UploadAsync(content, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType ?? "application/octet-stream" } }, cancellationToken);
        return Normalise(path);
    }

    public async Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var blob = await BlobAsync(path, cancellationToken);
        try
        {
            return await blob.OpenReadAsync(new BlobOpenReadOptions(allowModifications: false), cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new FileNotFoundException($"Document '{path}' is not in the store.", path, ex);
        }
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var blob = await BlobAsync(path, cancellationToken);
        return await blob.ExistsAsync(cancellationToken);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var blob = await BlobAsync(path, cancellationToken);
        await blob.DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    private async Task<BlobClient> BlobAsync(string path, CancellationToken cancellationToken)
    {
        var name = Normalise(path);
        if (name.Length == 0 || name.Split('/').Any(s => s is "." or ".." || s.Length == 0))
        {
            throw new ArgumentException($"'{path}' is not a valid store path.", nameof(path));
        }

        if (!_ready)
        {
            await _init.WaitAsync(cancellationToken);
            try
            {
                if (!_ready)
                {
                    await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
                    _ready = true;
                }
            }
            finally
            {
                _init.Release();
            }
        }

        return container.GetBlobClient(name);
    }

    private static string Normalise(string path) => (path ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');
}
