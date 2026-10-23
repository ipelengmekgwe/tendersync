namespace TenderSync.Application.Abstractions.Identity;

/// <summary>User store operations the Application needs. Implemented over ASP.NET Identity in Infrastructure.</summary>
public interface IIdentityService
{
    /// <summary>Validates credentials with lockout. Returns null when the email is unknown, the password is wrong or the account is locked.</summary>
    Task<AuthenticatedUser?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken = default);

    Task<AuthenticatedUser?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task RecordLoginAsync(Guid userId, DateTimeOffset at, CancellationToken cancellationToken = default);
}

public sealed record AuthenticatedUser(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles);
