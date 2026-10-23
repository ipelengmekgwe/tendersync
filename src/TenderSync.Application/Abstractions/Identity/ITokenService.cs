namespace TenderSync.Application.Abstractions.Identity;

/// <summary>Issues JWT access tokens and rotating refresh tokens (ADR-011). Refresh tokens are stored hashed.</summary>
public interface ITokenService
{
    Task<TokenPair> IssueAsync(AuthenticatedUser user, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Rotates a refresh token. Returns null when it is unknown, expired or revoked; reuse of a revoked token revokes the whole family.</summary>
    Task<TokenRefreshResult?> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default);

    Task RevokeAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default);
}

public sealed record TokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

public sealed record TokenRefreshResult(TokenPair Tokens, AuthenticatedUser User);
