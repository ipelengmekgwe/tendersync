using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Events;

public sealed record TenderStatusChanged(Guid TenderId, string Ocid, TenderStatus From, TenderStatus To, string? Reason, DateTimeOffset OccurredAt) : IDomainEvent;
