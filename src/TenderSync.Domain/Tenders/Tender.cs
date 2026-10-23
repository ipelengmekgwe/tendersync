using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Events;

namespace TenderSync.Domain.Tenders;

/// <summary>
/// A contracting process as seen across its releases. Aggregate root for releases, documents and status history.
/// Status is derived (the feed sends none); see <see cref="DeriveStatus"/>.
/// </summary>
public sealed class Tender : Entity
{
    public static readonly TimeSpan DefaultClosingSoonWindow = TimeSpan.FromDays(7);

    private readonly List<TenderDocument> _documents = [];
    private readonly List<TenderRelease> _releases = [];
    private readonly List<TenderStatusHistory> _statusHistory = [];

    private Tender()
    {
    }

    public string Ocid { get; private set; } = null!;

    public string ExternalTenderId { get; private set; } = null!;

    /// <summary>The buyer's bid reference, e.g. WCGHCC0038/1/2025. The OCDS feed puts this in tender.title.</summary>
    public string BidNumber { get; private set; } = null!;

    public string Title { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public Guid BuyerId { get; private set; }

    public Buyer? Buyer { get; private set; }

    public Province Province { get; private set; }

    public ProcurementCategory MainCategory { get; private set; }

    public string[] AdditionalCategories { get; private set; } = [];

    public string? ProcurementMethod { get; private set; }

    public string? ProcurementMethodDetails { get; private set; }

    public decimal? ValueAmount { get; private set; }

    public string? ValueCurrency { get; private set; }

    public DateTimeOffset PublishedAt { get; private set; }

    public DateTimeOffset? ClosingAt { get; private set; }

    public DateTimeOffset? BriefingAt { get; private set; }

    public bool? BriefingCompulsory { get; private set; }

    public string? BriefingVenue { get; private set; }

    public SubmissionMethod SubmissionMethod { get; private set; }

    public TenderStatus Status { get; private set; }

    public string? StatusReason { get; private set; }

    public string SourceUrl { get; private set; } = null!;

    public TenderSourceKind SourceKind { get; private set; }

    public string ContentHash { get; private set; } = null!;

    public bool HasAwards { get; private set; }

    public DateTimeOffset FirstSeenAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public DateTimeOffset? LastCheckedAt { get; private set; }

    public IReadOnlyCollection<TenderDocument> Documents => _documents.AsReadOnly();

    public IReadOnlyCollection<TenderRelease> Releases => _releases.AsReadOnly();

    public IReadOnlyCollection<TenderStatusHistory> StatusHistory => _statusHistory.AsReadOnly();

    public static Tender CreateFromSnapshot(TenderSnapshot snapshot, Guid buyerId, Province province, TenderSourceKind sourceKind, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var tender = new Tender
        {
            Ocid = Guard.NotEmpty(snapshot.Ocid, nameof(snapshot.Ocid)),
            ExternalTenderId = Guard.NotEmpty(snapshot.ExternalTenderId, nameof(snapshot.ExternalTenderId)),
            BuyerId = buyerId,
            SourceKind = sourceKind,
            FirstSeenAt = now,
            ContentHash = string.Empty,
        };

        tender.ApplyRelease(snapshot, province, now);
        tender.Raise(new TenderFirstSeen(tender.Id, tender.Ocid, now));
        return tender;
    }

    /// <summary>
    /// Applies a newly ingested release. Returns <see cref="ReleaseDiff.Unchanged"/> when the content hash matches,
    /// otherwise updates the tender, records the raw release, and raises <see cref="TenderChanged"/> for existing tenders.
    /// </summary>
    public ReleaseDiff ApplyRelease(TenderSnapshot snapshot, Province province, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!string.Equals(snapshot.Ocid, Ocid, StringComparison.Ordinal))
        {
            throw new DomainException($"Release for {snapshot.Ocid} cannot be applied to tender {Ocid}.");
        }

        LastSeenAt = now;
        var hash = ContentHasher.Compute(snapshot);
        var isNew = ContentHash.Length == 0;

        if (!isNew && string.Equals(hash, ContentHash, StringComparison.Ordinal))
        {
            return ReleaseDiff.Unchanged;
        }

        var diff = BuildDiff(snapshot, isNew);

        BidNumber = Guard.NotEmpty(snapshot.BidNumber, nameof(snapshot.BidNumber));
        Title = Guard.NotEmpty(snapshot.Title, nameof(snapshot.Title));
        Description = snapshot.Description?.Trim() ?? string.Empty;
        MainCategory = snapshot.MainCategory;
        AdditionalCategories = [.. snapshot.AdditionalCategories.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase)];
        ProcurementMethod = Guard.Trimmed(snapshot.ProcurementMethod);
        ProcurementMethodDetails = Guard.Trimmed(snapshot.ProcurementMethodDetails);
        ValueAmount = snapshot.ValueAmount is > 0 ? snapshot.ValueAmount : null;
        ValueCurrency = Guard.Trimmed(snapshot.ValueCurrency);
        PublishedAt = snapshot.PublishedAt;
        ClosingAt = snapshot.ClosingAt;
        SourceUrl = Guard.NotEmpty(snapshot.SourceUrl, nameof(snapshot.SourceUrl));
        HasAwards = snapshot.HasAwards;
        if (province != Province.Unknown || Province == Province.Unknown)
        {
            Province = province;
        }

        SyncDocuments(snapshot.Documents);

        if (_releases.TrueForAll(r => !string.Equals(r.Hash, hash, StringComparison.Ordinal)))
        {
            _releases.Add(TenderRelease.Create(Id, snapshot, hash));
        }

        ContentHash = hash;

        if (!isNew)
        {
            Raise(new TenderChanged(Id, Ocid, diff, now));
        }

        return diff;
    }

    /// <summary>
    /// Recomputes the derived status. Awarded and Cancelled are sticky and only set through
    /// <see cref="MarkAwarded"/> and <see cref="MarkCancelled"/>.
    /// </summary>
    public TenderStatus DeriveStatus(DateTimeOffset now, TimeSpan? closingSoonWindow = null)
    {
        if (Status is TenderStatus.Awarded or TenderStatus.Cancelled)
        {
            return Status;
        }

        if (HasAwards)
        {
            Transition(TenderStatus.Awarded, "awards published", now);
            return Status;
        }

        var window = closingSoonWindow ?? DefaultClosingSoonWindow;
        var derived = ClosingAt switch
        {
            null => TenderStatus.Unknown,
            var closing when closing <= now => TenderStatus.Closed,
            var closing when closing <= now + window => TenderStatus.ClosingSoon,
            _ => TenderStatus.Open,
        };

        Transition(derived, null, now);
        return Status;
    }

    public void MarkAwarded(DateTimeOffset now)
    {
        HasAwards = true;
        Transition(TenderStatus.Awarded, "awards published", now);
    }

    public void MarkCancelled(string reason, DateTimeOffset now)
    {
        Transition(TenderStatus.Cancelled, Guard.NotEmpty(reason, nameof(reason)), now);
    }

    public void SetBriefing(DateTimeOffset? at, bool? compulsory, string? venue)
    {
        BriefingAt = at;
        BriefingCompulsory = compulsory;
        BriefingVenue = Guard.Trimmed(venue);
    }

    public void SetSubmissionMethod(SubmissionMethod method) => SubmissionMethod = method;

    public void SetProvince(Province province)
    {
        if (province != Province.Unknown)
        {
            Province = province;
        }
    }

    public void MarkChecked(DateTimeOffset now) => LastCheckedAt = now;

    private ReleaseDiff BuildDiff(TenderSnapshot snapshot, bool isNew)
    {
        if (isNew)
        {
            return new ReleaseDiff(
                IsNew: true,
                NewDocumentIds: [.. snapshot.Documents.Select(d => d.ExternalId)],
                RemovedDocumentIds: [],
                ModifiedDocumentIds: [],
                OldClosingAt: null,
                NewClosingAt: snapshot.ClosingAt,
                TitleChanged: false,
                DescriptionChanged: false,
                AwardsPublished: snapshot.HasAwards);
        }

        var existing = _documents.ToDictionary(d => d.ExternalId, StringComparer.Ordinal);
        var incoming = snapshot.Documents.ToDictionary(d => d.ExternalId, StringComparer.Ordinal);

        var added = incoming.Keys.Where(k => !existing.ContainsKey(k)).Order(StringComparer.Ordinal).ToList();
        var removed = existing.Keys.Where(k => !incoming.ContainsKey(k)).Order(StringComparer.Ordinal).ToList();
        var modified = incoming
            .Where(kv => existing.TryGetValue(kv.Key, out var doc) && (doc.Title != kv.Value.Title || doc.DateModified != kv.Value.DateModified))
            .Select(kv => kv.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        return new ReleaseDiff(
            IsNew: false,
            NewDocumentIds: added,
            RemovedDocumentIds: removed,
            ModifiedDocumentIds: modified,
            OldClosingAt: ClosingAt,
            NewClosingAt: snapshot.ClosingAt,
            TitleChanged: !string.Equals(Title, snapshot.Title?.Trim(), StringComparison.Ordinal),
            DescriptionChanged: !string.Equals(Description, snapshot.Description?.Trim() ?? string.Empty, StringComparison.Ordinal),
            AwardsPublished: snapshot.HasAwards && !HasAwards);
    }

    private void SyncDocuments(IReadOnlyList<TenderDocumentSnapshot> documents)
    {
        foreach (var snapshot in documents)
        {
            var existing = _documents.Find(d => string.Equals(d.ExternalId, snapshot.ExternalId, StringComparison.Ordinal));
            if (existing is null)
            {
                _documents.Add(TenderDocument.Create(Id, snapshot));
            }
            else
            {
                existing.Apply(snapshot);
            }
        }

        // Documents withdrawn by the buyer are kept (with their files) so the history stays complete.
    }

    private void Transition(TenderStatus to, string? reason, DateTimeOffset now)
    {
        if (Status == to)
        {
            return;
        }

        var from = Status;
        Status = to;
        StatusReason = reason;
        _statusHistory.Add(new TenderStatusHistory(Id, from, to, reason, now));
        Raise(new TenderStatusChanged(Id, Ocid, from, to, reason, now));
    }
}
