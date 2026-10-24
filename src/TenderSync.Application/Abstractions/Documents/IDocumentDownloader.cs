namespace TenderSync.Application.Abstractions.Documents;

/// <summary>
/// Fetches a public bid-pack file over HTTP with the outbound throttling from PLAN.md §7.3 (two concurrent downloads,
/// 500 ms spacing) and the eTenders resilience pipeline. Implemented in Infrastructure; Application never touches HttpClient.
/// </summary>
public interface IDocumentDownloader
{
    /// <summary>
    /// Downloads the URL into a seekable temporary stream. Throws <see cref="DocumentDownloadException"/> when the host is
    /// not allowed, the response is not successful, or the body exceeds <paramref name="maxBytes"/>.
    /// </summary>
    Task<DownloadedFile> DownloadAsync(Uri url, long maxBytes, CancellationToken cancellationToken = default);
}

/// <summary>A downloaded body positioned at 0. Dispose it to delete the temporary file.</summary>
public sealed class DownloadedFile(Stream content, string? contentType, string? fileName, long length) : IAsyncDisposable, IDisposable
{
    public Stream Content { get; } = content;

    public string? ContentType { get; } = contentType;

    /// <summary>File name from Content-Disposition when the server sent one.</summary>
    public string? FileName { get; } = fileName;

    public long Length { get; } = length;

    public void Dispose() => Content.Dispose();

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

/// <summary>A download that failed for a reason the document should record (not a transient fault worth retrying the job for).</summary>
public class DocumentDownloadException : Exception
{
    public DocumentDownloadException()
    {
    }

    public DocumentDownloadException(string message)
        : base(message)
    {
    }

    public DocumentDownloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
