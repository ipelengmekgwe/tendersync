using TenderSync.Domain.Common;

namespace TenderSync.Domain.Notifications;

/// <summary>A domain event persisted in the same transaction as the change that raised it, drained by the Worker (ADR-005).</summary>
public sealed class OutboxMessage : Entity
{
    public const int MaxAttempts = 5;

    private OutboxMessage()
    {
    }

    /// <summary>Assembly-qualified-free event type name, e.g. "TenderSync.Domain.Events.TenderChanged".</summary>
    public string Type { get; private set; } = null!;

    public string PayloadJson { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? Error { get; private set; }

    public static OutboxMessage FromEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var type = domainEvent.GetType();
        return new OutboxMessage
        {
            Type = type.FullName ?? type.Name,
            PayloadJson = DomainJson.Serialize((object)domainEvent),
            OccurredAt = domainEvent.OccurredAt,
        };
    }

    public void MarkProcessed(DateTimeOffset now)
    {
        Attempts++;
        ProcessedAt = now;
        Error = null;
    }

    public bool MarkFailed(string error)
    {
        Attempts++;
        Error = Guard.NotEmpty(error, nameof(error));
        return Attempts < MaxAttempts;
    }
}
