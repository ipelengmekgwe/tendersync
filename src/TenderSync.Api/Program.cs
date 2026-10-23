// Composition root for the TenderSync API. WP02 adds auth, MediatR pipeline, OpenAPI/Scalar, Serilog and endpoints.
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));

await app.RunAsync();

/// <summary>Exposed so WebApplicationFactory in TenderSync.Api.Tests can host the app.</summary>
public partial class Program
{
}
