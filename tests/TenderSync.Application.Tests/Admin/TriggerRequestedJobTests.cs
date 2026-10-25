using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TenderSync.Application.Abstractions.Jobs;
using TenderSync.Application.Common.Events;
using TenderSync.Application.Features.Admin.EventHandlers;
using TenderSync.Domain.Events;

namespace TenderSync.Application.Tests.Admin;

public class TriggerRequestedJobTests
{
    [Fact]
    public async Task A_run_now_request_triggers_that_recurring_job_in_the_worker()
    {
        var scheduler = Substitute.For<IPipelineScheduler>();
        var handler = new TriggerRequestedJob(scheduler, NullLogger<TriggerRequestedJob>.Instance);

        await handler.Handle(new DomainEventNotification<JobRunRequested>(new JobRunRequested("ingest-recent", Guid.CreateVersion7(), "Admin", DateTimeOffset.UtcNow)), CancellationToken.None);

        scheduler.Received(1).TriggerRecurringJob("ingest-recent");
    }
}
