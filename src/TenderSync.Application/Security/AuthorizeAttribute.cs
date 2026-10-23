namespace TenderSync.Application.Security;

/// <summary>
/// Declares who may send a request. Checked by the authorization pipeline behaviour before validation.
/// <see cref="Role"/> is the minimum role; Admin satisfies every requirement, Reviewer satisfies Reviewer and Viewer.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AuthorizeAttribute : Attribute
{
    public AuthorizeAttribute()
    {
    }

    public AuthorizeAttribute(string role)
    {
        Role = role;
    }

    /// <summary>Minimum role required, or null for "any authenticated user".</summary>
    public string? Role { get; init; }
}

/// <summary>Marks a request that may be sent without authentication (login, refresh).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AllowAnonymousAttribute : Attribute;
