using Hangfire;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TenderSync.Worker.Infrastructure;

namespace TenderSync.Worker.Extensions;

public static class WorkerAppExtensions
{
    public const string DashboardPath = "/hangfire";

    public static IApplicationBuilder UseDashboardTokenCookie(this IApplicationBuilder app) => app.UseMiddleware<DashboardTokenCookieMiddleware>();

    /// <summary>Hangfire dashboard, gated by the ASP.NET Admin policy (JWT via header or cookie) rather than Hangfire's local-only default.</summary>
    public static IEndpointRouteBuilder MapWorkerDashboard(this IEndpointRouteBuilder app)
    {
        app.MapHangfireDashboard(DashboardPath, new DashboardOptions
        {
            DashboardTitle = "TenderSync jobs",
            Authorization = [new AuthenticatedByAspNetFilter()],
            IgnoreAntiforgeryToken = true,
            DisplayStorageConnectionString = false,
        }).RequireAuthorization(WorkerServiceExtensions.AdminPolicy);

        return app;
    }

    public static IEndpointRouteBuilder MapWorkerHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = WriteAsync }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready"), ResponseWriter = WriteAsync }).AllowAnonymous();
        return app;
    }

    private static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            totalDurationMs = (int)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), durationMs = (int)e.Value.Duration.TotalMilliseconds, error = e.Value.Exception?.Message }),
        });
    }
}
