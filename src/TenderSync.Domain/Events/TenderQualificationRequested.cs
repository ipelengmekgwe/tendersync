using TenderSync.Domain.Common;

namespace TenderSync.Domain.Events;

/// <summary>POST /tenders/{id}/qualify asked for a qualification run (PLAN.md §7.5); the Worker's outbox turns it into QualifyTenderJob.</summary>
public sealed record TenderQualificationRequested(Guid TenderId, string Ocid, Guid? CompanyProfileId, DateTimeOffset OccurredAt) : IDomainEvent;
