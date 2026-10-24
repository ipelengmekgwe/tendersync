using TenderSync.Application.Abstractions.Jobs;

namespace TenderSync.Application.Jobs;

/// <summary>
/// For hosts that do not run background jobs (the API). Only the Worker publishes outbox events, so the event handlers that
/// enqueue pipeline jobs never execute in the API; reaching this class means an event was published in the wrong host.
/// </summary>
public sealed class NoPipelineScheduler : IPipelineScheduler
{
    public void EnqueueDocumentDownload(Guid tenderId) => throw NotSupported();

    public void EnqueueTextExtraction(Guid documentId) => throw NotSupported();

    public void EnqueueClassification(Guid tenderId) => throw NotSupported();

    public void EnqueueRequirementExtraction(Guid tenderId) => throw NotSupported();

    private static InvalidOperationException NotSupported() =>
        new("This host does not run pipeline jobs. Domain events are dispatched by the Worker's outbox job, which registers the Hangfire scheduler.");
}
