using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Tenders;

/// <summary>A bid-pack file published with a tender and its download / extraction state.</summary>
public sealed class TenderDocument : Entity
{
    private TenderDocument()
    {
    }

    public Guid TenderId { get; private set; }

    public string ExternalId { get; private set; } = null!;

    public string Title { get; private set; } = null!;

    public string Url { get; private set; } = null!;

    public string? Format { get; private set; }

    public string? Language { get; private set; }

    public DateTimeOffset? DatePublished { get; private set; }

    public DateTimeOffset? DateModified { get; private set; }

    public DocumentStatus Status { get; private set; } = DocumentStatus.Pending;

    public string? StoragePath { get; private set; }

    public string? Sha256 { get; private set; }

    public long? SizeBytes { get; private set; }

    public int? PageCount { get; private set; }

    public bool NeedsOcr { get; private set; }

    public DateTimeOffset? DownloadedAt { get; private set; }

    public DateTimeOffset? ExtractedAt { get; private set; }

    public string? Error { get; private set; }

    internal static TenderDocument Create(Guid tenderId, TenderDocumentSnapshot snapshot)
    {
        var document = new TenderDocument
        {
            TenderId = tenderId,
            ExternalId = Guard.NotEmpty(snapshot.ExternalId, nameof(snapshot.ExternalId)),
        };
        document.Apply(snapshot);
        return document;
    }

    /// <summary>Returns true when the published metadata changed (title or modified date).</summary>
    internal bool Apply(TenderDocumentSnapshot snapshot)
    {
        var changed = Title is not null && (Title != snapshot.Title || DateModified != snapshot.DateModified);
        Title = Guard.NotEmpty(snapshot.Title, nameof(snapshot.Title));
        Url = Guard.NotEmpty(snapshot.Url, nameof(snapshot.Url));
        Format = Guard.Trimmed(snapshot.Format)?.ToLowerInvariant();
        Language = Guard.Trimmed(snapshot.Language);
        DatePublished = snapshot.DatePublished;
        DateModified = snapshot.DateModified;

        if (changed && Status != DocumentStatus.Pending)
        {
            // A re-published file must be downloaded and extracted again.
            Status = DocumentStatus.Pending;
            Error = null;
        }

        return changed;
    }

    public void MarkDownloaded(string storagePath, string sha256, long sizeBytes, DateTimeOffset now)
    {
        StoragePath = Guard.NotEmpty(storagePath, nameof(storagePath));
        Sha256 = Guard.NotEmpty(sha256, nameof(sha256));
        SizeBytes = sizeBytes;
        DownloadedAt = now;
        Status = DocumentStatus.Downloaded;
        Error = null;
    }

    public void MarkExtracted(int pageCount, bool needsOcr, DateTimeOffset now)
    {
        if (Status is not (DocumentStatus.Downloaded or DocumentStatus.Extracted))
        {
            throw new DomainException("A document must be downloaded before it can be extracted.");
        }

        PageCount = pageCount;
        NeedsOcr = needsOcr;
        ExtractedAt = now;
        Status = DocumentStatus.Extracted;
    }

    public void MarkFailed(string error)
    {
        Status = DocumentStatus.Failed;
        Error = Guard.NotEmpty(error, nameof(error));
    }

    public void MarkSkipped(string reason)
    {
        Status = DocumentStatus.Skipped;
        Error = Guard.NotEmpty(reason, nameof(reason));
    }
}
