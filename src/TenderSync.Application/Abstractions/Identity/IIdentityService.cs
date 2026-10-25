namespace TenderSync.Application.Abstractions.Identity;

/// <summary>User store operations the Application needs. Implemented over ASP.NET Identity in Infrastructure.</summary>
public interface IIdentityService
{
    /// <summary>Validates credentials with lockout. Returns null when the email is unknown, the password is wrong or the account is locked.</summary>
    Task<AuthenticatedUser?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken = default);

    Task<AuthenticatedUser?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task RecordLoginAsync(Guid userId, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>Every account, for /admin/users.</summary>
    Task<IReadOnlyList<UserAccount>> ListUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates an account with one role. Throws ConflictException for a taken email and a validation error for a weak password.</summary>
    Task<UserAccount> CreateUserAsync(string email, string displayName, string role, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the display name and the single role, disables or re-enables the account (disabling also revokes its refresh
    /// tokens) and, when <paramref name="newPassword"/> is given, replaces the password. Returns null for an unknown id.
    /// </summary>
    Task<UserAccount?> UpdateUserAsync(Guid userId, string displayName, string role, bool disabled, string? newPassword, CancellationToken cancellationToken = default);
}

public sealed record AuthenticatedUser(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles);

/// <summary>An account as the admin pages show it. Disabled accounts cannot sign in or refresh; LockedOutUntil is the temporary lockout after failed attempts.</summary>
public sealed record UserAccount(
    Guid Id,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    bool Disabled,
    DateTimeOffset? LockedOutUntil,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);
