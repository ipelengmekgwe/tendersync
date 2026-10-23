using MediatR;
using Microsoft.Extensions.Logging;
using TenderSync.Application.Common.Events;
using TenderSync.Domain.Events;

namespace TenderSync.Application.Features.Tenders.EventHandlers;

/// <summary>
/// First consumers of the outbox. WP07 (classification) and WP10 (notifications) add the real handlers next to these;
/// until then the events are only logged, which also proves the dispatch path end to end.
/// </summary>
public sealed class TenderFirstSeenLogger(ILogger<TenderFirstSeenLogger> logger) : INotificationHandler<DomainEventNotification<TenderFirstSeen>>
{
    public Task Handle(DomainEventNotification<TenderFirstSeen> notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Tender first seen: {Ocid} ({TenderId})", notification.Event.Ocid, notification.Event.TenderId);
        return Task.CompletedTask;
    }
}

public sealed class TenderChangedLogger(ILogger<TenderChangedLogger> logger) : INotificationHandler<DomainEventNotification<TenderChanged>>
{
    public Task Handle(DomainEventNotification<TenderChanged> notification, CancellationToken cancellationToken)
    {
        var diff = notification.Event.Diff;
        logger.LogInformation(
            "Tender changed: {Ocid} new docs {NewDocs}, closing changed {ClosingChanged}, awards {Awards}",
            notification.Event.Ocid, diff.NewDocumentIds.Count, diff.ClosingChanged, diff.AwardsPublished);
        return Task.CompletedTask;
    }
}

public sealed class TenderStatusChangedLogger(ILogger<TenderStatusChangedLogger> logger) : INotificationHandler<DomainEventNotification<TenderStatusChanged>>
{
    public Task Handle(DomainEventNotification<TenderStatusChanged> notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Tender {Ocid} status {From} → {To} ({Reason})", notification.Event.Ocid, notification.Event.From, notification.Event.To, notification.Event.Reason);
        return Task.CompletedTask;
    }
}
