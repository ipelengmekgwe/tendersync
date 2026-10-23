namespace TenderSync.Application.Security;

/// <summary>Application roles (ADR-011). Admin ⊃ Reviewer ⊃ Viewer in terms of what each may do.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Reviewer = "Reviewer";
    public const string Viewer = "Viewer";

    public static IReadOnlyList<string> All { get; } = [Admin, Reviewer, Viewer];
}
