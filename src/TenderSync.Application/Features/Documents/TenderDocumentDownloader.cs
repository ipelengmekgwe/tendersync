using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Documents;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Abstractions.Storage;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Documents;

public sealed record DocumentDownloadSummary(
    Guid TenderId,
    string Ocid,
    int Attempted,
    int Downloaded,
    int Skipped,
    int Failed,
    IReadOnlyList<Guid> DownloadedDocumentIds,
    IReadOnlyList<string> Errors);

/// <summary>
/// Downloads every pending document of a tender (PLAN.md §7.3 steps 1–3): format and host checks, size cap, magic-byte
/// sniffing, sha256, then <see cref="IDocumentStore"/> at tenders/{ocid}/{documentId}.{ext}. One SaveChanges per document so
/// a crash mid-tender keeps what was stored. Documents already Downloaded or Extracted are left alone.
/// </summary>
public sealed class TenderDocumentDownloader(
    IAppDbContext db,
    IDocumentDownloader downloader,
    IDocumentStore store,
    IOptions<DocumentOptions> options,
    IDateTimeProvider clock,
    ILogger<TenderDocumentDownloader> logger)
{
    private const int SniffLength = 512;

    public async Task<DocumentDownloadSummary> DownloadAsync(Guid tenderId, CancellationToken cancellationToken = default)
    {
        var tender = await db.Tenders.GetWithDetailsAsync(tenderId, cancellationToken)
            ?? throw new NotFoundException(nameof(Tender), tenderId);

        int attempted = 0, downloaded = 0, skipped = 0, failed = 0;
        var downloadedIds = new List<Guid>();
        var errors = new List<string>();

        foreach (var document in tender.Documents.Where(d => d.Status is DocumentStatus.Pending or DocumentStatus.Failed).OrderBy(d => d.DatePublished).ThenBy(d => d.Title, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempted++;
            try
            {
                switch (await DownloadOneAsync(tender, document, cancellationToken))
                {
                    case DocumentStatus.Downloaded:
                        downloaded++;
                        downloadedIds.Add(document.Id);
                        break;
                    case DocumentStatus.Skipped:
                        skipped++;
                        break;
                    default:
                        failed++;
                        errors.Add($"{document.Title}: {document.Error}");
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                var message = Truncate(ex.Message);
                errors.Add($"{document.Title}: {message}");
                document.MarkFailed(message);
                logger.LogError(ex, "Download of {Title} ({DocumentId}) for {Ocid} failed", document.Title, document.Id, tender.Ocid);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("Documents for {Ocid}: attempted {Attempted}, downloaded {Downloaded}, skipped {Skipped}, failed {Failed}", tender.Ocid, attempted, downloaded, skipped, failed);
        return new DocumentDownloadSummary(tender.Id, tender.Ocid, attempted, downloaded, skipped, failed, downloadedIds, errors);
    }

    private async Task<DocumentStatus> DownloadOneAsync(Tender tender, TenderDocument document, CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (!Uri.TryCreate(document.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            document.MarkSkipped("Document URL is not an absolute http(s) URL.");
            return DocumentStatus.Skipped;
        }

        if (settings.AllowedDownloadHosts.Length > 0 && !settings.AllowedDownloadHosts.Contains(url.Host, StringComparer.OrdinalIgnoreCase))
        {
            document.MarkSkipped($"Host '{url.Host}' is not in Documents:AllowedDownloadHosts.");
            return DocumentStatus.Skipped;
        }

        var declared = DocumentFormats.Normalise(document.Format, document.Title, url);
        if (declared is not null && settings.SkippedFormats.Contains(declared, StringComparer.OrdinalIgnoreCase))
        {
            document.MarkSkipped($"Format '{declared}' is not downloaded (audio/video).");
            return DocumentStatus.Skipped;
        }

        await using var file = await downloader.DownloadAsync(url, settings.MaxDocumentBytes, cancellationToken);

        var head = new byte[SniffLength];
        var read = await file.Content.ReadAtLeastAsync(head, SniffLength, throwOnEndOfStream: false, cancellationToken);
        file.Content.Position = 0;
        var format = DocumentFormats.Sniff(head.AsSpan(0, read), declared ?? DocumentFormats.Normalise(file.ContentType, file.FileName));

        if (format == DocumentFormats.Html)
        {
            document.MarkFailed("The server returned an HTML page instead of the document.");
            return DocumentStatus.Failed;
        }

        if (read == 0)
        {
            document.MarkFailed("The server returned an empty body.");
            return DocumentStatus.Failed;
        }

        if (format != declared)
        {
            logger.LogWarning("{Ocid} document {Title} is declared {Declared} but looks like {Sniffed}; storing as {Sniffed}", tender.Ocid, document.Title, declared, format, format);
        }

        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(file.Content, cancellationToken));
        file.Content.Position = 0;

        var path = DocumentFormats.StoragePath(tender.Ocid, document.Id, format);
        var stored = await store.SaveAsync(path, file.Content, DocumentFormats.ContentTypeFor(format), cancellationToken);
        document.MarkDownloaded(stored, hash, file.Length, clock.UtcNow);
        return DocumentStatus.Downloaded;
    }

    private static string Truncate(string message) => message.Length <= 2000 ? message : message[..2000];
}
