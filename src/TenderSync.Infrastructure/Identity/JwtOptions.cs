using System.ComponentModel.DataAnnotations;

namespace TenderSync.Infrastructure.Identity;

/// <summary>JWT settings (ADR-011). The signing key comes from user-secrets, environment or Key Vault, never from appsettings.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = "TenderSync";

    [Required]
    public string Audience { get; set; } = "TenderSync";

    /// <summary>At least 32 characters; HMAC-SHA256 key material.</summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 14;
}
