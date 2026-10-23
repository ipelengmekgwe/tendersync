namespace TenderSync.Domain.Tenders;

/// <summary>One document attached to a tender release, as published by the source.</summary>
public sealed record TenderDocumentSnapshot(
    string ExternalId,
    string Title,
    string Url,
    string? Format,
    string? Language,
    DateTimeOffset? DatePublished,
    DateTimeOffset? DateModified);
