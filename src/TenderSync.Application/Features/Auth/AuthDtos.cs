namespace TenderSync.Application.Features.Auth;

public sealed record UserDto(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles);

public sealed record AuthResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt, UserDto User);
