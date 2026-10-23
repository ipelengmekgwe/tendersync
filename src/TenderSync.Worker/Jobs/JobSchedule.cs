using Hangfire;
using TenderSync.Application.Common;

namespace TenderSync.Worker.Jobs;

/// <summary>
/// The single place recurring jobs are declared (PLAN.md §13). Cron expressions are in SAST. Later work packages add their
/// jobs here: ingest (WP03), status/reminders (WP11), source health (WP03), digests (WP10).
/// </summary>
public static class JobSchedule
{
    public static void Register(IRecurringJobManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        var options = new RecurringJobOptions { TimeZone = SouthAfricaTime.Zone };

        // Queues come from each job's [Queue] attribute (applied when the job is enqueued), which works on every storage;
        // the recurring-job metadata queue is not persisted by Hangfire.PostgreSql 1.20, so it is not passed here.
        manager.AddOrUpdate<ProcessOutboxJob>(ProcessOutboxJob.RecurringId, job => job.ExecuteAsync(CancellationToken.None), Cron.Minutely(), options);
    }

    /// <summary>Recurring job ids that must exist after <see cref="Register"/>; checked by tests and the readiness view.</summary>
    public static IReadOnlyList<string> ExpectedRecurringJobIds { get; } = [ProcessOutboxJob.RecurringId];
}
