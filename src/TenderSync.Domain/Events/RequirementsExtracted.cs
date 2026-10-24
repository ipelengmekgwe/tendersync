using TenderSync.Domain.Common;

namespace TenderSync.Domain.Events;

/// <summary>Requirement extraction replaced the tender's requirement rows (PLAN.md §7.4); qualification (WP09) chains on it.</summary>
public sealed record RequirementsExtracted(Guid TenderId, string Ocid, int RequirementCount, DateTimeOffset OccurredAt) : IDomainEvent;
