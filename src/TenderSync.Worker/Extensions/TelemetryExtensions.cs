using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Resources;
using TenderSync.Infrastructure.Notifications;

namespace TenderSync.Worker.Extensions;

public static class TelemetryExtensions
{
    public const string ConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    /// <summary>
    /// Application Insights (ADR-0008) when APPLICATIONINSIGHTS_CONNECTION_STRING is set, as the Azure deployment does:
    /// dashboard requests, outgoing HTTP calls (eTenders, document downloads, LLM providers, channels) and, through the
    /// OpenTelemetry logger provider, every Serilog event. Channel URLs that carry secrets in their path are recorded
    /// redacted. Mirrors the API host's extension; the hosts may not share code.
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
