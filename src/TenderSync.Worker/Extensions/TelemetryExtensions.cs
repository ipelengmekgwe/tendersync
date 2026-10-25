using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Resources;

namespace TenderSync.Worker.Extensions;

public static class TelemetryExtensions
{
    public const string ConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    /// <summary>
    /// Application Insights (ADR-0008) when APPLICATIONINSIGHTS_CONNECTION_STRING is set, as the Azure deployment does:
    /// dashboard requests, outgoing HTTP calls (eTenders, document downloads, LLM providers, channels) and, through the
    /// OpenTelemetry logger provider, every Serilog event. Mirrors the API host's extension; the hosts may not share code.
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
        return true;
    }
}
