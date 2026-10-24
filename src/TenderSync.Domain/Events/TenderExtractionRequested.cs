using TenderSync.Domain.Common;

namespace TenderSync.Domain.Events;

/// <summary>A reviewer asked for requirement extraction (POST /tenders/{id}/extract); the Worker enqueues the job.</summary>
public sealed record TenderExtractionRequested(Guid TenderId, string Ocid, DateTimeOffset OccurredAt) : IDomainEvent;
