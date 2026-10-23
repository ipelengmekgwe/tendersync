// Composition root for the TenderSync Worker. WP05 adds Hangfire server, queues, recurring jobs and the dashboard.
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));

await app.RunAsync();

/// <summary>Exposed for test hosting.</summary>
public partial class Program
{
}
