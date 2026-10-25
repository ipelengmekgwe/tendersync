using MediatR;
using Microsoft.Extensions.Logging;
using TenderSync.Application.Abstractions.Jobs;
using TenderSync.Application.Common.Events;
using TenderSync.Domain.Events;

namespace TenderSync.Application.Features.Admin.EventHandlers;

/// <summary>Runs in the Worker from the outbox: starts the recurring job an admin asked for. Triggering twice only runs it twice, which every job tolerates.</summary>
public sealed class TriggerRequestedJob(IPipelineScheduler scheduler, ILogger<TriggerRequestedJob> logger)
    : INotificationHandler<DomainEventNotification<JobRunRequested>>
{
    public Task Handle(DomainEventNotification<JobRunRequested> notification, CancellationToken cancellationToken)
    {
        var evt = notification.Event;
        logger.LogInformation("Running {JobId} now, as requested by {User}", evt.JobId, evt.RequestedByName);
        scheduler.TriggerRecurringJob(evt.JobId);
        return Task.CompletedTask;
    }
}
