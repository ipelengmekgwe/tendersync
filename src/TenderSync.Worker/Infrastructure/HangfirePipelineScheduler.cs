using Hangfire;
using TenderSync.Application.Abstractions.Jobs;
using TenderSync.Worker.Jobs;

namespace TenderSync.Worker.Infrastructure;

/// <summary>Implements the Application scheduler port over Hangfire; the [Queue] attribute on each job class routes it to the pipeline queue.</summary>
internal sealed class HangfirePipelineScheduler(IBackgroundJobClient jobs) : IPipelineScheduler
{
    public void EnqueueDocumentDownload(Guid tenderId) =>
        jobs.Enqueue<DownloadTenderDocumentsJob>(job => job.ExecuteAsync(tenderId, CancellationToken.None));

    public void EnqueueTextExtraction(Guid documentId) =>
        jobs.Enqueue<ExtractDocumentTextJob>(job => job.ExecuteAsync(documentId, CancellationToken.None));
}
