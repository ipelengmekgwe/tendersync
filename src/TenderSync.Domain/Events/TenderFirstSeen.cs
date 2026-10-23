using TenderSync.Domain.Common;

namespace TenderSync.Domain.Events;

public sealed record TenderFirstSeen(Guid TenderId, string Ocid, DateTimeOffset OccurredAt) : IDomainEvent;
