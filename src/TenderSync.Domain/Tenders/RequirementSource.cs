namespace TenderSync.Domain.Tenders;

/// <summary>Where a requirement was read from: document, page and the quoted text. Owned by <see cref="TenderRequirement"/>.</summary>
public sealed record RequirementSource(Guid? DocumentId, int? Page, string? Quote);
