using TenderSync.Domain.Common;

namespace TenderSync.Domain.Events;

public sealed record TenderMatched(Guid TenderId, Guid CompanyProfileId, Guid MatchId, int Relevance, DateTimeOffset OccurredAt) : IDomainEvent;
