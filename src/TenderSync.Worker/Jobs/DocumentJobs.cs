using Hangfire;
using TenderSync.Application.Features.Documents;
using TenderSync.Application.Jobs;

namespace TenderSync.Worker.Jobs;

/// <summary>
/// Downloads every pending bid-pack file of one tender (PLAN.md §7.3) and chains <see cref="ExtractDocumentTextJob"/> for
/// each stored file. Enqueued by the document event handlers; never recurring.
/// </summary>
[Queue(JobQueues.Pipeline)]
[AutomaticRetry(Attempts = 2, DelaysInSeconds = [120, 600])]
public sealed class DownloadTenderDocumentsJob(TenderDocumentDownloader downloader, IBackgroundJobClient jobs, JobRunner runner)
{
    public Task ExecuteAsync(Guid tenderId, CancellationToken cancellationToken) =>
        runner.RunAsync(nameof(DownloadTenderDocumentsJob), async ct =>
        {
            var summary = await downloader.DownloadAsync(tenderId, ct);
            foreach (var documentId in summary.DownloadedDocumentIds)
            {
                jobs.Enqueue<ExtractDocumentTextJob>(job => job.ExecuteAsync(documentId, CancellationToken.None));
            }

            return new JobResult(summary.Downloaded, summary);
        }, cancellationToken);
}

/// <summary>Extracts the text of one downloaded document into document_texts and applies the needs_ocr heuristic.</summary>
[Queue(JobQueues.Pipeline)]
[AutomaticRetry(Attempts = 1, DelaysInSeconds = [60])]
public sealed class ExtractDocumentTextJob(DocumentTextExtraction extraction, JobRunner runner)
{
    public Task ExecuteAsync(Guid documentId, CancellationToken cancellationToken) =>
        runner.RunAsync(nameof(ExtractDocumentTextJob), async ct =>
        {
            var result = await extraction.ExtractAsync(documentId, ct);
            return new JobResult(result.Pages, result);
        }, cancellationToken);
}
