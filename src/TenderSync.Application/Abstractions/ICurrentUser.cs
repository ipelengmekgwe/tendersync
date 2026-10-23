namespace TenderSync.Application.Abstractions;

/// <summary>The caller of the current request: claims in the API, the system identity in the Worker.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    string? Email { get; }

    string DisplayName { get; }

    IReadOnlyCollection<string> Roles { get; }

    string? IpAddress { get; }
}
