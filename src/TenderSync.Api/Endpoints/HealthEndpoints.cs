using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TenderSync.Api.Endpoints;

public static class HealthEndpoints
{
    /// <summary>/health/live answers as soon as the process runs; /health/ready also checks the database.</summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteAsync,
        }).WithTags("Health").AllowAnonymous();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteAsync,
        }).WithTags("Health").AllowAnonymous();

        return app;
    }

    private static Task WriteAsync(HttpContext context, HealthReport report)
    {
        // The endpoints are anonymous: exception text (host names, addresses) is shown only in Development and logged elsewhere.
        var showErrors = context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment();
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            totalDurationMs = (int)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), durationMs = (int)e.Value.Duration.TotalMilliseconds, error = showErrors ? e.Value.Exception?.Message : null }),
        });
    }
}
