using TenderSync.Domain.Common;

namespace TenderSync.Domain.Events;

/// <summary>An admin asked for a recurring job to run now (PLAN.md §11 /admin/pipeline). The API writes it to the outbox; the Worker triggers the job.</summary>
public sealed record JobRunRequested(string JobId, Guid? RequestedBy, string RequestedByName, DateTimeOffset OccurredAt) : IDomainEvent;
