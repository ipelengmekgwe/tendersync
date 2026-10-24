using MediatR;
using Microsoft.Extensions.Logging;
using TenderSync.Application.Abstractions.Jobs;
using TenderSync.Application.Common.Events;
using TenderSync.Domain.Events;

namespace TenderSync.Application.Features.Classification.EventHandlers;

/// <summary>Every new tender is classified (PLAN.md §7.2); a changed title or description is classified again. Runs in the Worker from the outbox.</summary>
public sealed class ClassifyNewTenders(IPipelineScheduler scheduler, ILogger<ClassifyNewTenders> logger) : INotificationHandler<DomainEventNotification<TenderFirstSeen>>
{
    public Task Handle(DomainEventNotification<TenderFirstSeen> notification, CancellationToken cancellationToken)
    {
        logger.LogDebug("Queuing classification for new tender {Ocid}", notification.Event.Ocid);
        scheduler.EnqueueClassification(notification.Event.TenderId);
        return Task.CompletedTask;
    }
}

public sealed class ReclassifyChangedTenders(IPipelineScheduler scheduler, ILogger<ReclassifyChangedTenders> logger) : INotificationHandler<DomainEventNotification<TenderChanged>>
{
    public Task Handle(DomainEventNotification<TenderChanged> notification, CancellationToken cancellationToken)
    {
        var diff = notification.Event.Diff;
        if (!diff.TitleChanged && !diff.DescriptionChanged)
        {
            return Task.CompletedTask;
        }

        logger.LogInformation("Tender {Ocid} text changed; queuing reclassification", notification.Event.Ocid);
        scheduler.EnqueueClassification(notification.Event.TenderId);
        return Task.CompletedTask;
    }
}
