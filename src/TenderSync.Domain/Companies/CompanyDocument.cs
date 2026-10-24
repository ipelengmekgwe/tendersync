using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Companies;

/// <summary>A document the company keeps for bids. Stored encrypted (ADR-013); soft-deleted, never hard-deleted.</summary>
public sealed class CompanyDocument : Entity
{
    private CompanyDocument()
    {
    }

    public Guid CompanyProfileId { get; private set; }

    public CompanyDocumentType Type { get; private set; }

    public string Title { get; private set; } = null!;

    public string StoragePath { get; private set; } = null!;

    /// <summary>Per-file data key, wrapped by the master key (base64).</summary>
    public string EncryptedDataKey { get; private set; } = null!;

    public string Sha256 { get; private set; } = null!;

    public long SizeBytes { get; private set; }

    public string? ContentType { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public static CompanyDocument Create(Guid companyProfileId, CompanyDocumentType type, string title, string storagePath, string encryptedDataKey, string sha256, long sizeBytes, string? contentType, DateTimeOffset? expiresAt)
    {
        return new CompanyDocument
        {
            CompanyProfileId = companyProfileId,
            Type = type,
            Title = Guard.NotEmpty(title, nameof(title)),
            StoragePath = Guard.NotEmpty(storagePath, nameof(storagePath)),
            EncryptedDataKey = Guard.NotEmpty(encryptedDataKey, nameof(encryptedDataKey)),
            Sha256 = Guard.NotEmpty(sha256, nameof(sha256)),
            SizeBytes = sizeBytes,
            ContentType = Guard.Trimmed(contentType),
            ExpiresAt = expiresAt,
        };
    }

    public bool IsExpired(DateTimeOffset asOf) => ExpiresAt is not null && ExpiresAt <= asOf;

    public bool ExpiresWithin(TimeSpan window, DateTimeOffset asOf) => ExpiresAt is not null && ExpiresAt > asOf && ExpiresAt <= asOf + window;

    public void Rename(string title) => Title = Guard.NotEmpty(title, nameof(title));

    public void SetExpiry(DateTimeOffset? expiresAt) => ExpiresAt = expiresAt;

    public void SoftDelete(DateTimeOffset now) => DeletedAt ??= now;

    /// <summary>Key rotation: the same data key wrapped by the new master key (docs/runbooks/key-rotation.md).</summary>
    public void RewrapDataKey(string encryptedDataKey) => EncryptedDataKey = Guard.NotEmpty(encryptedDataKey, nameof(encryptedDataKey));
}
