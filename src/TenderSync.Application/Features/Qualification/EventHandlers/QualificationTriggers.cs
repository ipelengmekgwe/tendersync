using MediatR;
using Microsoft.Extensions.Logging;
using TenderSync.Application.Abstractions.Jobs;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Events;
using TenderSync.Domain.Events;

namespace TenderSync.Application.Features.Qualification.EventHandlers;

/// <summary>POST /tenders/{id}/qualify raised the event; the Worker's outbox turns it into QualifyTenderJob.</summary>
public sealed class QualifyWhenRequested(IPipelineScheduler scheduler, ILogger<QualifyWhenRequested> logger) : INotificationHandler<DomainEventNotification<TenderQualificationRequested>>
{
    public Task Handle(DomainEventNotification<TenderQualificationRequested> notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Qualification requested for {Ocid}", notification.Event.Ocid);
        scheduler.EnqueueQualification(notification.Event.TenderId, notification.Event.CompanyProfileId);
        return Task.CompletedTask;
    }
}

/// <summary>The pipeline chain (PLAN.md §13): extracted requirements of a matched tender are qualified for every matched profile.</summary>
public sealed class QualifyWhenRequirementsExtracted(IAppDbContext db, IPipelineScheduler scheduler, ILogger<QualifyWhenRequirementsExtracted> logger)
    : INotificationHandler<DomainEventNotification<RequirementsExtracted>>
{
    public async Task Handle(DomainEventNotification<RequirementsExtracted> notification, CancellationToken cancellationToken)
    {
        var evt = notification.Event;
        if (evt.RequirementCount == 0)
        {
            return;
        }

        var matches = await db.TenderMatches.ListByTenderAsync(evt.TenderId, cancellationToken);
        if (matches.Count == 0)
        {
            logger.LogDebug("Tender {Ocid} has requirements but no match; qualification waits for a request", evt.Ocid);
            return;
        }

        logger.LogInformation("Requirements of matched tender {Ocid} extracted; queuing qualification", evt.Ocid);
        scheduler.EnqueueQualification(evt.TenderId, null);
    }
}
