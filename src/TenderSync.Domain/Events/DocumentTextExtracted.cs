using TenderSync.Domain.Common;

namespace TenderSync.Domain.Events;

/// <summary>A bid-pack document's text is in document_texts; the pipeline checks whether the tender is ready for requirement extraction.</summary>
public sealed record DocumentTextExtracted(Guid TenderId, Guid DocumentId, int PageCount, bool NeedsOcr, DateTimeOffset OccurredAt) : IDomainEvent;
