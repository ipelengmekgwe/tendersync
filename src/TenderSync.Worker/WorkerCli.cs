using System.Globalization;
using TenderSync.Worker.Jobs;

namespace TenderSync.Worker;

/// <summary>
/// One-shot command line modes for operators (PLAN.md WP03): <c>--ingest-days N</c> ingests the last N days and exits;
/// <c>--backfill-days N</c> does the same in weekly slices; <c>--probe</c> runs the source health check. The Hangfire server
/// is not started in these modes.
/// </summary>
public sealed record WorkerCli(int? IngestDays, int? BackfillDays, bool Probe)
{
    public bool IsOneShot => IngestDays is not null || BackfillDays is not null || Probe;

    public static WorkerCli Parse(string[] args)
    {
        int? ingest = null, backfill = null;
        var probe = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ingest-days" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var d):
                    ingest = d;
                    i++;
                    break;
                case "--backfill-days" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b):
                    backfill = b;
                    i++;
                    break;
                case "--probe":
                    probe = true;
                    break;
            }
        }

        return new WorkerCli(ingest, backfill, probe);
    }

    public async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();

        if (Probe)
        {
            await scope.ServiceProvider.GetRequiredService<SourceHealthCheckJob>().ExecuteAsync(cancellationToken);
        }

        if (IngestDays is { } days)
        {
            await scope.ServiceProvider.GetRequiredService<IngestRecentReleasesJob>().ExecuteAsync(days, cancellationToken);
        }

        if (BackfillDays is { } backfillDays)
        {
            await scope.ServiceProvider.GetRequiredService<BackfillReleasesJob>().ExecuteAsync(backfillDays, cancellationToken);
        }
    }
}
