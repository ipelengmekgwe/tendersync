namespace TenderSync.Application.Security;

/// <summary>Admin ⊃ Reviewer ⊃ Viewer. A user satisfies a required role when any of their roles ranks at or above it.</summary>
public static class RoleHierarchy
{
    private static readonly Dictionary<string, int> Rank = new(StringComparer.OrdinalIgnoreCase)
    {
        [Roles.Viewer] = 1,
        [Roles.Reviewer] = 2,
        [Roles.Admin] = 3,
    };

    public static bool Satisfies(IEnumerable<string> userRoles, string requiredRole)
    {
        if (!Rank.TryGetValue(requiredRole, out var required))
        {
            return false;
        }

        return userRoles.Any(r => Rank.TryGetValue(r, out var rank) && rank >= required);
    }

    /// <summary>Roles that satisfy the required role, for ASP.NET Core policies.</summary>
    public static IReadOnlyList<string> RolesAtOrAbove(string requiredRole)
    {
        var required = Rank[requiredRole];
        return [.. Rank.Where(kv => kv.Value >= required).Select(kv => kv.Key)];
    }
}
