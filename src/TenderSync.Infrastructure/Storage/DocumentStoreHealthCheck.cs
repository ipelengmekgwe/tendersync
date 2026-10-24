using System.Text;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TenderSync.Application.Abstractions.Storage;

namespace TenderSync.Infrastructure.Storage;

/// <summary>Readiness (PLAN.md §10): a tiny write/read/delete round trip proves the store is reachable and writable.</summary>
public sealed class DocumentStoreHealthCheck(IDocumentStore store) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var path = $"health/probe-{Guid.NewGuid():N}.txt";
        try
        {
            using var content = new MemoryStream(Encoding.UTF8.GetBytes("ok"));
            await store.SaveAsync(path, content, "text/plain", cancellationToken);
            var exists = await store.ExistsAsync(path, cancellationToken);
            await store.DeleteAsync(path, cancellationToken);
            return exists ? HealthCheckResult.Healthy(store.GetType().Name) : HealthCheckResult.Unhealthy("Probe file was not found after writing.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Document store unavailable", ex);
        }
    }
}
