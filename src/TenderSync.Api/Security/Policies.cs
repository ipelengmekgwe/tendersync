namespace TenderSync.Api.Security;

/// <summary>Authorization policy names. Each requires the named role or a higher one (see RoleHierarchy).</summary>
public static class Policies
{
    public const string Viewer = "Viewer";
    public const string Reviewer = "Reviewer";
    public const string Admin = "Admin";
}
