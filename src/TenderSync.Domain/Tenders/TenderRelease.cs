using TenderSync.Domain.Common;

namespace TenderSync.Domain.Tenders;

/// <summary>Raw copy of one release as received from the source. Kept for audit and re-processing.</summary>
public sealed class TenderRelease : Entity
{
    private TenderRelease()
    {
    }

    public Guid TenderId { get; private set; }

    public string ReleaseId { get; private set; } = null!;

    public DateTimeOffset ReleaseDate { get; private set; }

    public string Tag { get; private set; } = null!;

    /// <summary>Raw JSON of the release (jsonb).</summary>
    public string RawJson { get; private set; } = null!;

    public string Hash { get; private set; } = null!;

    internal static TenderRelease Create(Guid tenderId, TenderSnapshot snapshot, string hash)
    {
        return new TenderRelease
        {
            TenderId = tenderId,
            ReleaseId = Guard.NotEmpty(snapshot.ReleaseId, nameof(snapshot.ReleaseId)),
            ReleaseDate = snapshot.ReleaseDate,
            Tag = snapshot.ReleaseTag,
            RawJson = snapshot.RawJson,
            Hash = hash,
        };
    }
}
