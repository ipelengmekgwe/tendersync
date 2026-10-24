using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TenderSync.Application.Features.Documents;
using TenderSync.Infrastructure.Persistence;
using TenderSync.Worker.Jobs;

namespace TenderSync.Worker;

/// <summary>
/// One-shot command line modes for operators (PLAN.md WP03/WP04): <c>--ingest-days N</c> ingests the last N days and exits;
/// <c>--backfill-days N</c> does the same in weekly slices; <c>--probe</c> runs the source health check;
/// <c>--download-documents ocid[,ocid...]</c> downloads and extracts the bid packs of those tenders inline (no Hangfire).
/// The Hangfire server is not started in these modes.
/// </summary>
public sealed record WorkerCli(int? IngestDays, int? BackfillDays, bool Probe, IReadOnlyList<string> DownloadDocumentsFor)
{
    public bool IsOneShot => IngestDays is not null || BackfillDays is not null || Probe || DownloadDocumentsFor.Count > 0;

    public static WorkerCli Parse(string[] args)
    {
        int? ingest = null, backfill = null;
        var probe = false;
        var ocids = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ingest-days" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var d):
                    ingest = d;
                    i++;
                    break;
                case "--backfill-days" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b):
                    backfill = b;
                    i++;
                    break;
                case "--probe":
                    probe = true;
                    break;
                case "--download-documents" when i + 1 < args.Length:
                    ocids.AddRange(args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    i++;
                    break;
            }
        }

        return new WorkerCli(ingest, backfill, probe, ocids);
    }

    public async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();

        if (Probe)
        {
            await scope.ServiceProvider.GetRequiredService<SourceHealthCheckJob>().ExecuteAsync(cancellationToken);
        }

        if (IngestDays is { } days)
        {
            await scope.ServiceProvider.GetRequiredService<IngestRecentReleasesJob>().ExecuteAsync(days, cancellationToken);
        }

        if (BackfillDays is { } backfillDays)
        {
            await scope.ServiceProvider.GetRequiredService<BackfillReleasesJob>().ExecuteAsync(backfillDays, cancellationToken);
        }

        if (DownloadDocumentsFor.Count > 0)
        {
            await DownloadDocumentsAsync(scope.ServiceProvider, cancellationToken);
        }
    }

    /// <summary>Download then extract, synchronously, printing one line per document so an operator can judge the acceptance rate.</summary>
    private async Task DownloadDocumentsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var context = services.GetRequiredService<AppDbContext>();
        var downloader = services.GetRequiredService<TenderDocumentDownloader>();
        var extraction = services.GetRequiredService<DocumentTextExtraction>();
        var logger = services.GetRequiredService<ILogger<WorkerCli>>();

        var ids = await context.TenderSet.AsNoTracking().Where(t => DownloadDocumentsFor.Contains(t.Ocid)).Select(t => new { t.Id, t.Ocid }).ToListAsync(cancellationToken);
        foreach (var missing in DownloadDocumentsFor.Except(ids.Select(t => t.Ocid), StringComparer.Ordinal))
        {
            logger.LogWarning("Tender {Ocid} is not in the database; ingest it first", missing);
        }

        int documents = 0, downloaded = 0, extracted = 0, needsOcr = 0, skipped = 0, failed = 0;
        foreach (var tender in ids)
        {
            var summary = await downloader.DownloadAsync(tender.Id, cancellationToken);
            documents += summary.Attempted;
            downloaded += summary.Downloaded;
            skipped += summary.Skipped;
            failed += summary.Failed;
            foreach (var error in summary.Errors)
            {
                logger.LogWarning("{Ocid}: {Error}", tender.Ocid, error);
            }

            foreach (var documentId in summary.DownloadedDocumentIds)
            {
                var result = await extraction.ExtractAsync(documentId, cancellationToken);
                switch (result.Status)
                {
                    case Domain.Enums.DocumentStatus.Extracted:
                        extracted++;
                        needsOcr += result.NeedsOcr ? 1 : 0;
                        break;
                    case Domain.Enums.DocumentStatus.Skipped:
                        skipped++;
                        break;
                    default:
                        failed++;
                        break;
                }

                logger.LogInformation("{Ocid} {DocumentId}: {Status}, {Pages} page(s), {Chars} chars, needs OCR {NeedsOcr} {Error}", tender.Ocid, documentId, result.Status, result.Pages, result.Chars, result.NeedsOcr, result.Error);
            }
        }

        logger.LogInformation(
            "Documents for {Tenders} tender(s): {Documents} pending, {Downloaded} downloaded, {Extracted} extracted ({NeedsOcr} need OCR), {Skipped} skipped, {Failed} failed",
            ids.Count, documents, downloaded, extracted, needsOcr, skipped, failed);
    }
}
