using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Identity;
using TenderSync.Infrastructure.Persistence;

namespace TenderSync.Infrastructure.Identity;

/// <summary>HMAC-SHA256 JWT access tokens plus rotating, hashed refresh tokens persisted in refresh_tokens.</summary>
internal sealed class JwtTokenService(AppDbContext context, UserManager<ApplicationUser> userManager, IOptions<JwtOptions> options, IDateTimeProvider clock) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public async Task<TokenPair> IssueAsync(AuthenticatedUser user, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var (refreshToken, refreshHash) = NewRefreshToken();
        var refreshExpires = now.AddDays(_options.RefreshTokenDays);

        context.Set<RefreshToken>().Add(new RefreshToken(user.Id, refreshHash, now, refreshExpires, ipAddress));
        await context.SaveChangesAsync(cancellationToken);

        var (accessToken, accessExpires) = CreateAccessToken(user, now);
        return new TokenPair(accessToken, accessExpires, refreshToken, refreshExpires);
    }

    public async Task<TokenRefreshResult?> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var hash = Hash(refreshToken);
        var existing = await context.Set<RefreshToken>().FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (existing.IsRevoked)
        {
            // Reuse of a rotated token means it leaked: revoke every active token for the user.
            var family = await context.Set<RefreshToken>().Where(t => t.UserId == existing.UserId && t.RevokedAt == null).ToListAsync(cancellationToken);
            foreach (var token in family)
            {
                token.Revoke(now, ipAddress);
            }

            await context.SaveChangesAsync(cancellationToken);
            return null;
        }

        if (existing.IsExpired(now))
        {
            return null;
        }

        var user = await LoadUserAsync(existing.UserId);
        if (user is null)
        {
            return null;
        }

        var (newToken, newHash) = NewRefreshToken();
        var newExpires = now.AddDays(_options.RefreshTokenDays);
        existing.Revoke(now, ipAddress, newHash);
        context.Set<RefreshToken>().Add(new RefreshToken(user.Id, newHash, now, newExpires, ipAddress));
        await context.SaveChangesAsync(cancellationToken);

        var (accessToken, accessExpires) = CreateAccessToken(user, now);
        return new TokenRefreshResult(new TokenPair(accessToken, accessExpires, newToken, newExpires), user);
    }

    public async Task RevokeAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var hash = Hash(refreshToken);
        var existing = await context.Set<RefreshToken>().FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (existing is null || existing.IsRevoked)
        {
            return;
        }

        existing.Revoke(clock.UtcNow, ipAddress);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<AuthenticatedUser?> LoadUserAsync(Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || user.LockoutEnd > clock.UtcNow)
        {
            return null;
        }

        var roles = await userManager.GetRolesAsync(user);
        return new AuthenticatedUser(user.Id, user.Email ?? string.Empty, user.DisplayName ?? user.Email ?? user.Id.ToString(), [.. roles]);
    }

    private (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(AuthenticatedUser user, DateTimeOffset now)
    {
        var expires = now.AddMinutes(_options.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString("N")),
            new(JwtClaims.Email, user.Email),
            new(JwtClaims.Name, user.DisplayName),
        };
        claims.AddRange(user.Roles.Select(r => new Claim(JwtClaims.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now.UtcDateTime,
            IssuedAt = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };

        var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };
        return (handler.CreateToken(descriptor), expires);
    }

    private static (string Token, string Hash) NewRefreshToken()
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));
        return (token, Hash(token));
    }

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
