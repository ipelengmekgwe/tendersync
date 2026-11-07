using MediatR;
using Microsoft.Extensions.Logging;
using TenderSync.Application.Abstractions.Jobs;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Events;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Events;

namespace TenderSync.Application.Features.Requirements.EventHandlers;

/// <summary>POST /tenders/{id}/extract raised the event; the Worker's outbox turns it into ExtractRequirementsJob.</summary>
public sealed class ExtractWhenRequested(IPipelineScheduler scheduler, ILogger<ExtractWhenRequested> logger) : INotificationHandler<DomainEventNotification<TenderExtractionRequested>>
{
    public Task Handle(DomainEventNotification<TenderExtractionRequested> notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Requirement extraction requested for {Ocid}", notification.Event.Ocid);
        scheduler.EnqueueRequirementExtraction(notification.Event.TenderId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// The pipeline chain (PLAN.md §13): once no document of a matched tender is still in flight and at least one has text,
/// extract its requirements. Runs when a document gets its text and when one ends without text (failed or skipped), so
/// whichever document finishes last triggers it. Enqueueing twice is harmless because the extractor replaces the rows.
/// </summary>
public sealed class ExtractWhenDocumentsAreReady(IAppDbContext db, IPipelineScheduler scheduler, ILogger<ExtractWhenDocumentsAreReady> logger)
    : INotificationHandler<DomainEventNotification<DocumentTextExtracted>>, INotificationHandler<DomainEventNotification<DocumentNotExtracted>>
{
    public Task Handle(DomainEventNotification<DocumentTextExtracted> notification, CancellationToken cancellationToken) =>
        CheckAsync(notification.Event.TenderId, cancellationToken);

    public Task Handle(DomainEventNotification<DocumentNotExtracted> notification, CancellationToken cancellationToken) =>
        CheckAsync(notification.Event.TenderId, cancellationToken);

    private async Task CheckAsync(Guid tenderId, CancellationToken cancellationToken)
    {
        var matches = await db.TenderMatches.ListByTenderAsync(tenderId, cancellationToken);
        if (matches.Count == 0)
        {
            return;
        }

        var tender = await db.Tenders.GetWithDetailsAsync(tenderId, cancellationToken);
        if (tender is null)
        {
            return;
        }

        var pending = tender.Documents.Count(d => d.Status is DocumentStatus.Pending or DocumentStatus.Downloaded);
        if (pending > 0)
        {
            logger.LogDebug("Tender {Ocid} still has {Pending} document(s) in flight; extraction waits", tender.Ocid, pending);
            return;
        }

        if (!tender.Documents.Any(d => d.Status == DocumentStatus.Extracted))
        {
            logger.LogWarning("No document of matched tender {Ocid} has text; requirement extraction cannot run until one is downloaded", tender.Ocid);
            return;
        }

        logger.LogInformation("All documents of matched tender {Ocid} are processed; queuing requirement extraction", tender.Ocid);
        scheduler.EnqueueRequirementExtraction(tender.Id);
    }
}
