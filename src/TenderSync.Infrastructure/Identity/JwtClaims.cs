namespace TenderSync.Infrastructure.Identity;

/// <summary>Claim names used in TenderSync access tokens. Shared by the token service and the API's claim mapping.</summary>
public static class JwtClaims
{
    public const string Role = "role";
    public const string Name = "name";
    public const string Email = "email";
}
