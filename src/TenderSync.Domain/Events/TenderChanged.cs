using TenderSync.Domain.Common;
using TenderSync.Domain.Tenders;

namespace TenderSync.Domain.Events;

public sealed record TenderChanged(Guid TenderId, string Ocid, ReleaseDiff Diff, DateTimeOffset OccurredAt) : IDomainEvent;
