using MediatR;
using Microsoft.Extensions.Logging;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Events;

namespace TenderSync.Application.Jobs;

/// <summary>
/// Drains outbox_messages: deserialises each domain event, publishes it as a <see cref="DomainEventNotification{TEvent}"/>
/// and marks the row processed. A failing handler marks the row failed and moves on; after MaxAttempts the row is left for inspection.
/// </summary>
public sealed class OutboxProcessor(IAppDbContext db, IPublisher publisher, IDateTimeProvider clock, ILogger<OutboxProcessor> logger)
{
    public const int DefaultBatchSize = 100;

    public async Task<int> ProcessAsync(int batchSize = DefaultBatchSize, CancellationToken cancellationToken = default)
    {
        var messages = await db.Outbox.GetUnprocessedAsync(batchSize, cancellationToken);
        var processed = 0;

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var domainEvent = DomainEventTypeRegistry.Deserialize(message.Type, message.PayloadJson);
                if (domainEvent is null)
                {
                    message.MarkFailed($"Unknown or undeserialisable event type '{message.Type}'.");
                    logger.LogError("Outbox message {MessageId} has unknown event type {EventType}", message.Id, message.Type);
                }
                else
                {
                    await publisher.Publish(DomainEventNotification.Create(domainEvent), cancellationToken);
                    message.MarkProcessed(clock.UtcNow);
                    processed++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var willRetry = message.MarkFailed(ex.Message);
                logger.LogError(ex, "Outbox message {MessageId} ({EventType}) failed on attempt {Attempt}; retry: {WillRetry}", message.Id, message.Type, message.Attempts, willRetry);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return processed;
    }
}
