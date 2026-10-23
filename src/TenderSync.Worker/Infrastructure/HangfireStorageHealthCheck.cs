using Hangfire;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TenderSync.Worker.Infrastructure;

/// <summary>Readiness: Hangfire storage answers and reports its queues.</summary>
internal sealed class HangfireStorageHealthCheck(JobStorage storage) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var api = storage.GetMonitoringApi();
            var servers = api.Servers().Count;
            var enqueued = api.Queues().Sum(q => q.Length);
            return Task.FromResult(HealthCheckResult.Healthy($"servers={servers}, enqueued={enqueued}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Hangfire storage unavailable", ex));
        }
    }
}
