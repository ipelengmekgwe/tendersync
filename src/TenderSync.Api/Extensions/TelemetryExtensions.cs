using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Resources;
using TenderSync.Infrastructure.Notifications;

namespace TenderSync.Api.Extensions;

public static class TelemetryExtensions
{
    public const string ConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    /// <summary>
    /// Application Insights (ADR-0008) when APPLICATIONINSIGHTS_CONNECTION_STRING is set, as the Azure deployment does:
    /// requests, outgoing HTTP calls and, through the OpenTelemetry logger provider, every Serilog event. Returns whether it
    /// was added so <c>UseSerilog</c> can forward events to the providers. The setting decides what gets registered, so it is
    /// read here rather than lazily; tests never set it. The default console provider is removed because Serilog already
    /// writes the console and forwarding would print every line twice. Outgoing-call URLs that carry channel secrets in
    /// their path (Telegram bot token, webhook URLs) are recorded redacted (<see cref="SecretUrlRedactor"/>).
    /// </summary>
    public static bool AddAzureMonitorWhenConfigured(this WebApplicationBuilder builder, string serviceName)
    {
        if (string.IsNullOrWhiteSpace(builder.Configuration[ConnectionStringKey]))
        {
            return false;
        }

        builder.Logging.ClearProviders();
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .UseAzureMonitor();
        builder.Services.Configure<HttpClientTraceInstrumentationOptions>(options => options.EnrichWithHttpRequestMessage = (activity, request) =>
        {
            if (request.RequestUri is { } uri && SecretUrlRedactor.Redact(uri) is { } redacted)
            {
                activity.SetTag("url.full", redacted);
            }
        });
        return true;
    }
}
