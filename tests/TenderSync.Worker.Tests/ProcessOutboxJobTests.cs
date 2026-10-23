using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Domain.Common;
using TenderSync.Domain.Events;
using TenderSync.Domain.Notifications;
using TenderSync.Infrastructure.Persistence;
using TenderSync.Worker.Jobs;

namespace TenderSync.Worker.Tests;

[Collection(WorkerCollection.Name)]
[Trait("Category", "Integration")]
public class ProcessOutboxJobTests(WorkerFactory factory)
{
    private sealed record UnknownEvent(string Payload, DateTimeOffset OccurredAt) : IDomainEvent;

    [Fact]
    public async Task Dispatches_pending_events_marks_them_processed_and_records_a_job_run()
    {
        var evt = new TenderFirstSeen(Guid.CreateVersion7(), $"ocds-9t57fa-{Random.Shared.Next(100000, 999999)}", DateTimeOffset.UtcNow);
        Guid messageId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var message = OutboxMessage.FromEvent(evt);
            messageId = message.Id;
            db.Outbox.Add(message);
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProcessOutboxJob>().ExecuteAsync(CancellationToken.None);
        }

        factory.Recorded.Events.Should().Contain(e => e.Ocid == evt.Ocid && e.TenderId == evt.TenderId);

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await context.OutboxMessageSet.AsNoTracking().SingleAsync(m => m.Id == messageId);
            stored.ProcessedAt.Should().NotBeNull();
            stored.Error.Should().BeNull();

            var run = await context.JobRunSet.AsNoTracking().Where(r => r.JobName == nameof(ProcessOutboxJob)).OrderByDescending(r => r.StartedAt).FirstAsync();
            run.Success.Should().BeTrue();
            run.FinishedAt.Should().NotBeNull();
            run.ItemsProcessed.Should().BeGreaterThanOrEqualTo(1);
        }
    }

    [Fact]
    public async Task Unknown_event_types_are_marked_failed_without_stopping_the_batch()
    {
        Guid unknownId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var unknown = OutboxMessage.FromEvent(new UnknownEvent("x", DateTimeOffset.UtcNow));
            unknownId = unknown.Id;
            db.Outbox.Add(unknown);
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProcessOutboxJob>().ExecuteAsync(CancellationToken.None);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await context.OutboxMessageSet.AsNoTracking().SingleAsync(m => m.Id == unknownId);
            stored.ProcessedAt.Should().BeNull();
            stored.Attempts.Should().Be(1);
            stored.Error.Should().Contain("Unknown");
        }
    }
}
