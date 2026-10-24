using Hangfire;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Abstractions.Sources;
using TenderSync.Application.Features.Ingest;
using TenderSync.Application.Jobs;
using TenderSync.Domain.Operations;
using TenderSync.Infrastructure.ETenders;

namespace TenderSync.Worker.Jobs;

/// <summary>Rolling-window ingest every 30 minutes (PLAN.md §7.1): today − RecentWindowDays .. today + 1.</summary>
[Queue(JobQueues.Ingest)]
[AutomaticRetry(Attempts = 2, DelaysInSeconds = [60, 300])]
[DisableConcurrentExecution(timeoutInSeconds: 25 * 60)]
public sealed class IngestRecentReleasesJob(TenderIngestor ingestor, JobRunner runner, IOptions<ETendersOptions> options, IDateTimeProvider clock)
{
    public const string RecurringId = "ingest-recent-releases";

    public Task ExecuteAsync(CancellationToken cancellationToken) => ExecuteAsync(options.Value.RecentWindowDays, cancellationToken);

    public Task ExecuteAsync(int days, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        return runner.RunAsync(nameof(IngestRecentReleasesJob), async ct =>
        {
            var summary = await ingestor.IngestWindowAsync(today.AddDays(-days), today.AddDays(1), ct);
            return new JobResult(summary.Processed, summary);
        }, cancellationToken);
    }
}

/// <summary>Weekly backfill (Sunday 02:00 SAST) in slices so a flaky page never loses the whole window.</summary>
[Queue(JobQueues.Ingest)]
[AutomaticRetry(Attempts = 1)]
[DisableConcurrentExecution(timeoutInSeconds: 6 * 60 * 60)]
public sealed class BackfillReleasesJob(TenderIngestor ingestor, JobRunner runner, IOptions<ETendersOptions> options, IDateTimeProvider clock)
{
    public const string RecurringId = "backfill-releases";

    public Task ExecuteAsync(CancellationToken cancellationToken) => ExecuteAsync(options.Value.BackfillDays, cancellationToken);

    public Task ExecuteAsync(int days, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var slice = options.Value.BackfillSliceDays;

        return runner.RunAsync(nameof(BackfillReleasesJob), async ct =>
        {
            var summaries = new List<IngestSummary>();
            for (var end = today.AddDays(1); end > today.AddDays(-days); end = end.AddDays(-slice))
            {
                var start = end.AddDays(-slice);
                if (start < today.AddDays(-days))
                {
                    start = today.AddDays(-days);
                }

                summaries.Add(await ingestor.IngestWindowAsync(start, end, ct));
            }

            return new JobResult(summaries.Sum(s => s.Processed), new
            {
                slices = summaries.Count,
                fetched = summaries.Sum(s => s.Fetched),
                created = summaries.Sum(s => s.Created),
                changed = summaries.Sum(s => s.Changed),
                unchanged = summaries.Sum(s => s.Unchanged),
                failed = summaries.Sum(s => s.Failed),
            });
        }, cancellationToken);
    }
}

/// <summary>Reachability, latency and schema-drift probe every 15 minutes; unhealthy results raise PipelineFailure via the entity.</summary>
[Queue(JobQueues.Ingest)]
[AutomaticRetry(Attempts = 0)]
[DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
public sealed class SourceHealthCheckJob(ITenderSource source, IAppDbContext db, JobRunner runner, IDateTimeProvider clock)
{
    public const string RecurringId = "source-health-check";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        runner.RunAsync(nameof(SourceHealthCheckJob), async ct =>
        {
            var probe = await source.ProbeAsync(ct);
            db.SourceHealthChecks.Add(SourceHealthCheck.Record(
                probe.Source, probe.Ok, probe.HttpStatus, probe.LatencyMs,
                probe.HasDrift ? Domain.Common.DomainJson.Serialize(probe.SchemaDrift) : null,
                probe.Error, clock.UtcNow));
            await db.SaveChangesAsync(ct);
            return new JobResult(1, probe);
        }, cancellationToken);
}
