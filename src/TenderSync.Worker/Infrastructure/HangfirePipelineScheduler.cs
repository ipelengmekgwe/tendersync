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

    public void EnqueueClassification(Guid tenderId) =>
        jobs.Enqueue<ClassifyTenderJob>(job => job.ExecuteAsync(tenderId, CancellationToken.None));

    public void EnqueueRequirementExtraction(Guid tenderId) =>
        jobs.Enqueue<ExtractRequirementsJob>(job => job.ExecuteAsync(tenderId, CancellationToken.None));

    public void EnqueueQualification(Guid tenderId, Guid? companyProfileId) =>
        jobs.Enqueue<QualifyTenderJob>(job => job.ExecuteAsync(tenderId, companyProfileId, CancellationToken.None));

    public void EnqueueNotificationSend(Guid notificationId) =>
        jobs.Enqueue<SendNotificationJob>(job => job.ExecuteAsync(notificationId, CancellationToken.None));
}
