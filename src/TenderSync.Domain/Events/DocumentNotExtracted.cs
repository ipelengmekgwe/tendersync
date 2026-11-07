using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Events;

/// <summary>
/// A bid-pack document ended without text: its download or extraction failed, or it was skipped. The pipeline checks the
/// tender again, so a document that fails after the others are done cannot hold requirement extraction back.
/// </summary>
public sealed record DocumentNotExtracted(Guid TenderId, Guid DocumentId, DocumentStatus Status, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;
