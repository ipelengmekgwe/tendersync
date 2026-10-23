namespace TenderSync.Domain.Common;

/// <summary>Something that happened in the domain. Persisted to the outbox and dispatched by the Worker.</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
