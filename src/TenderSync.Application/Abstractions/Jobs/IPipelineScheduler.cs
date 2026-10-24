namespace TenderSync.Application.Abstractions.Jobs;

/// <summary>
/// Enqueues pipeline jobs (PLAN.md §13: pipeline queue, enqueued and chained). The Worker implements it over Hangfire;
/// the API registers <see cref="TenderSync.Application.Jobs.NoPipelineScheduler"/> because it never runs the outbox.
/// </summary>
public interface IPipelineScheduler
{
    /// <summary>Queues <c>DownloadTenderDocumentsJob</c> for the tender; the job skips documents that are already stored.</summary>
    void EnqueueDocumentDownload(Guid tenderId);

    /// <summary>Queues <c>ExtractDocumentTextJob</c> for one downloaded document.</summary>
    void EnqueueTextExtraction(Guid documentId);

    /// <summary>Queues <c>ClassifyTenderJob</c> (PLAN.md §7.2) for a new or changed tender.</summary>
    void EnqueueClassification(Guid tenderId);

    /// <summary>Queues <c>ExtractRequirementsJob</c> (PLAN.md §7.4).</summary>
    void EnqueueRequirementExtraction(Guid tenderId);
}
