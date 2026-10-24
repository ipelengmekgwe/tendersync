using TenderSync.Domain.Common;

namespace TenderSync.Domain.Events;

/// <summary>A user asked for the bid-pack documents of a tender to be downloaded (PLAN.md §7.3: an Admin or Reviewer can trigger any tender).</summary>
public sealed record TenderDocumentsRequested(Guid TenderId, string Ocid, int PendingDocuments, DateTimeOffset OccurredAt) : IDomainEvent;
