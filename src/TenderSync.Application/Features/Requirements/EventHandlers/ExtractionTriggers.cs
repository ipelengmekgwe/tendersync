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
/// The pipeline chain (PLAN.md §13): once the last document of a matched tender has text, extract its requirements.
/// Runs on every DocumentTextExtracted event; enqueueing twice is harmless because the extractor replaces the rows.
/// </summary>
public sealed class ExtractWhenDocumentsAreReady(IAppDbContext db, IPipelineScheduler scheduler, ILogger<ExtractWhenDocumentsAreReady> logger)
    : INotificationHandler<DomainEventNotification<DocumentTextExtracted>>
{
    public async Task Handle(DomainEventNotification<DocumentTextExtracted> notification, CancellationToken cancellationToken)
    {
        var evt = notification.Event;
        var matches = await db.TenderMatches.ListByTenderAsync(evt.TenderId, cancellationToken);
        if (matches.Count == 0)
        {
            return;
        }

        var tender = await db.Tenders.GetWithDetailsAsync(evt.TenderId, cancellationToken);
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

        logger.LogInformation("All documents of matched tender {Ocid} are processed; queuing requirement extraction", tender.Ocid);
        scheduler.EnqueueRequirementExtraction(tender.Id);
    }
}
