using Hangfire.Dashboard;

namespace TenderSync.Worker.Infrastructure;

/// <summary>
/// Hangfire's own authorization hook. Authorization already happened in the ASP.NET endpoint policy (Admin role via JWT),
/// so this only re-checks that a user is present rather than falling back to Hangfire's local-requests-only default.
/// </summary>
internal sealed class AuthenticatedByAspNetFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => context.GetHttpContext().User.Identity?.IsAuthenticated == true;
}
