namespace TenderSync.Infrastructure.Identity;

/// <summary>A rotating refresh token. Only the SHA-256 hash is stored; the family is revoked when a revoked token is reused.</summary>
public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public RefreshToken(Guid userId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt, string? createdByIp)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        CreatedByIp = createdByIp;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public string? CreatedByIp { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevokedByIp { get; private set; }

    public string? ReplacedByTokenHash { get; private set; }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;

    public bool IsRevoked => RevokedAt is not null;

    public bool IsActive(DateTimeOffset now) => !IsRevoked && !IsExpired(now);

    public void Revoke(DateTimeOffset now, string? ip, string? replacedByTokenHash = null)
    {
        RevokedAt ??= now;
        RevokedByIp ??= ip;
        ReplacedByTokenHash ??= replacedByTokenHash;
    }
}
