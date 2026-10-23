namespace TenderSync.Domain.Tenders;

/// <summary>What changed between the stored tender and a newly ingested release. Serialised into the outbox.</summary>
public sealed record ReleaseDiff(
    bool IsNew,
    IReadOnlyList<string> NewDocumentIds,
    IReadOnlyList<string> RemovedDocumentIds,
    IReadOnlyList<string> ModifiedDocumentIds,
    DateTimeOffset? OldClosingAt,
    DateTimeOffset? NewClosingAt,
    bool TitleChanged,
    bool DescriptionChanged,
    bool AwardsPublished)
{
    public static ReleaseDiff Unchanged { get; } = new(false, [], [], [], null, null, false, false, false);

    public bool ClosingChanged => OldClosingAt != NewClosingAt;

    public bool HasChanges =>
        IsNew || NewDocumentIds.Count > 0 || RemovedDocumentIds.Count > 0 || ModifiedDocumentIds.Count > 0
        || ClosingChanged || TitleChanged || DescriptionChanged || AwardsPublished;
}
