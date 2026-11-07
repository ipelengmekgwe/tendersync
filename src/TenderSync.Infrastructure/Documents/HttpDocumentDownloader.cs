using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Documents;
using TenderSync.Application.Features.Documents;

namespace TenderSync.Infrastructure.Documents;

/// <summary>
/// Streams a bid-pack file into a self-deleting temporary file under the throttle. The named client carries the
/// resilience pipeline (retry on transient failures, timeouts); a non-success status or an oversized body becomes a
/// <see cref="DocumentDownloadException"/> the caller records on the document.
/// </summary>
public sealed class HttpDocumentDownloader(IHttpClientFactory httpClientFactory, DownloadThrottle throttle, IOptions<DocumentOptions> options, ILogger<HttpDocumentDownloader> logger) : IDocumentDownloader
{
    public const string ClientName = "etenders-download";
    private const int MaxRedirects = 5;

    public async Task<DownloadedFile> DownloadAsync(Uri url, long maxBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        using var lease = await throttle.AcquireAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.DownloadTimeoutSeconds));
        var ct = timeout.Token;

        var client = httpClientFactory.CreateClient(ClientName);
        using var response = await GetFollowingAllowedRedirectsAsync(client, url, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new DocumentDownloadException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase} from {url.Host}.");
        }

        var declaredLength = response.Content.Headers.ContentLength;
        if (declaredLength > maxBytes)
        {
            throw new DocumentDownloadException($"File is {declaredLength:N0} bytes; the limit is {maxBytes:N0} bytes.");
        }

        var temp = Path.Combine(Path.GetTempPath(), $"tendersync-{Guid.NewGuid():N}.download");
        var file = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await body.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    throw new DocumentDownloadException($"File exceeds the limit of {maxBytes:N0} bytes.");
                }

                await file.WriteAsync(buffer.AsMemory(0, read), ct);
            }

            await file.FlushAsync(ct);
            file.Position = 0;
            logger.LogDebug("Downloaded {Bytes} bytes from {Url}", total, url);
            return new DownloadedFile(file, response.Content.Headers.ContentType?.MediaType, FileNameOf(response.Content.Headers.ContentDisposition), total);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await file.DisposeAsync();
            throw new DocumentDownloadException($"Download timed out after {options.Value.DownloadTimeoutSeconds} s.");
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// The client does not follow redirects itself (AllowAutoRedirect is off in AddDocumentProcessing): every hop is checked
    /// against <see cref="DocumentOptions.AllowsDownloadFrom"/>, so an allowed URL cannot bounce the Worker to another host.
    /// </summary>
    private async Task<HttpResponseMessage> GetFollowingAllowedRedirectsAsync(HttpClient client, Uri url, CancellationToken cancellationToken)
    {
        var current = url;
        for (var hop = 0; ; hop++)
        {
            var response = await client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308))
            {
                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null || hop >= MaxRedirects)
            {
                throw new DocumentDownloadException(location is null ? $"Redirect from {current.Host} without a location." : $"More than {MaxRedirects} redirects from {url.Host}.");
            }

            var next = location.IsAbsoluteUri ? location : new Uri(current, location);
            if (!options.Value.AllowsDownloadFrom(next))
            {
                throw new DocumentDownloadException($"Redirect to '{next.Host}' is not allowed (Documents:AllowedDownloadHosts).");
            }

            current = next;
        }
    }

    public static string? FileNameOf(ContentDispositionHeaderValue? disposition)
    {
        var name = disposition?.FileNameStar ?? disposition?.FileName;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        name = name.Trim().Trim('"');
        return name.Length == 0 ? null : Path.GetFileName(Uri.UnescapeDataString(name));
    }
}
