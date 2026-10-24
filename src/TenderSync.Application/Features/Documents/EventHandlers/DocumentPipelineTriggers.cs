using MediatR;
using Microsoft.Extensions.Logging;
using TenderSync.Application.Abstractions.Jobs;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Events;
using TenderSync.Domain.Events;

namespace TenderSync.Application.Features.Documents.EventHandlers;

/// <summary>
/// PLAN.md §7.3 step 1: documents are downloaded for matched tenders (cost control) and on explicit request. Handlers run in
/// the Worker from the outbox job; enqueueing twice is harmless because the download job skips stored documents.
/// </summary>
public sealed class DownloadDocumentsWhenRequested(IPipelineScheduler scheduler, ILogger<DownloadDocumentsWhenRequested> logger)
    : INotificationHandler<DomainEventNotification<TenderDocumentsRequested>>
{
    public Task Handle(DomainEventNotification<TenderDocumentsRequested> notification, CancellationToken cancellationToken)
    {
        var evt = notification.Event;
        logger.LogInformation("Document download requested for {Ocid}: {Pending} document(s) queued", evt.Ocid, evt.PendingDocuments);
        scheduler.EnqueueDocumentDownload(evt.TenderId);
        return Task.CompletedTask;
    }
}

public sealed class DownloadDocumentsWhenMatched(IPipelineScheduler scheduler, ILogger<DownloadDocumentsWhenMatched> logger)
    : INotificationHandler<DomainEventNotification<TenderMatched>>
{
    public Task Handle(DomainEventNotification<TenderMatched> notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Tender {TenderId} matched (relevance {Relevance}); queuing document download", notification.Event.TenderId, notification.Event.Relevance);
        scheduler.EnqueueDocumentDownload(notification.Event.TenderId);
        return Task.CompletedTask;
    }
}

/// <summary>New or re-published files on a tender that already has matches are fetched too, so requirement extraction sees the latest bid pack.</summary>
public sealed class DownloadChangedDocumentsOfMatchedTenders(IAppDbContext db, IPipelineScheduler scheduler, ILogger<DownloadChangedDocumentsOfMatchedTenders> logger)
    : INotificationHandler<DomainEventNotification<TenderChanged>>
{
    public async Task Handle(DomainEventNotification<TenderChanged> notification, CancellationToken cancellationToken)
    {
        var evt = notification.Event;
        if (evt.Diff.NewDocumentIds.Count == 0 && evt.Diff.ModifiedDocumentIds.Count == 0)
        {
            return;
        }

        var matches = await db.TenderMatches.ListByTenderAsync(evt.TenderId, cancellationToken);
        if (matches.Count == 0)
        {
            return;
        }

        logger.LogInformation("Matched tender {Ocid} has {New} new and {Modified} modified document(s); queuing download", evt.Ocid, evt.Diff.NewDocumentIds.Count, evt.Diff.ModifiedDocumentIds.Count);
        scheduler.EnqueueDocumentDownload(evt.TenderId);
    }
}
