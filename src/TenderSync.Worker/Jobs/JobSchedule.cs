using Hangfire;
using Microsoft.Extensions.Logging;
using TenderSync.Application.Common;

namespace TenderSync.Worker.Jobs;

/// <summary>
/// The single place recurring jobs are declared (PLAN.md §13). Cron expressions are in SAST. Later work packages add their
/// jobs here. Queues come from each job's [Queue] attribute.
/// </summary>
public static class JobSchedule
{
    public static void Register(IRecurringJobManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        var options = new RecurringJobOptions { TimeZone = SouthAfricaTime.Zone };

        manager.AddOrUpdate<ProcessOutboxJob>(ProcessOutboxJob.RecurringId, job => job.ExecuteAsync(CancellationToken.None), Cron.Minutely(), options);
        manager.AddOrUpdate<IngestRecentReleasesJob>(IngestRecentReleasesJob.RecurringId, job => job.ExecuteAsync(CancellationToken.None), "*/30 * * * *", options);
        manager.AddOrUpdate<BackfillReleasesJob>(BackfillReleasesJob.RecurringId, job => job.ExecuteAsync(CancellationToken.None), Cron.Weekly(DayOfWeek.Sunday, 2), options);
        manager.AddOrUpdate<SourceHealthCheckJob>(SourceHealthCheckJob.RecurringId, job => job.ExecuteAsync(CancellationToken.None), "*/15 * * * *", options);
        manager.AddOrUpdate<LlmCostRollupJob>(LlmCostRollupJob.RecurringId, job => job.ExecuteAsync(CancellationToken.None), "55 23 * * *", options);
        manager.AddOrUpdate<SweepNotificationsJob>(SweepNotificationsJob.RecurringId, job => job.ExecuteAsync(CancellationToken.None), Cron.Minutely(), options);
        manager.AddOrUpdate<DailyDigestJob>(DailyDigestJob.RecurringId, job => job.ExecuteAsync(CancellationToken.None), "0 7 * * *", options);
        manager.AddOrUpdate<RefreshActiveTendersJob>(RefreshActiveTendersJob.RecurringId, job => job.ExecuteAsync(CancellationToken.None), "0 5 * * *", options);
        manager.AddOrUpdate<ComputeStatusJob>(ComputeStatusJob.RecurringId, job => job.ExecuteAsync(CancellationToken.None), Cron.Hourly(), options);
        manager.AddOrUpdate<ClosingRemindersJob>(ClosingRemindersJob.RecurringId, job => job.ExecuteAsync(CancellationToken.None), "0 6 * * *", options);
        manager.AddOrUpdate<BriefingRemindersJob>(BriefingRemindersJob.RecurringId, job => job.ExecuteAsync(CancellationToken.None), "0 6 * * *", options);
        manager.AddOrUpdate<DocumentExpiryCheckJob>(DocumentExpiryCheckJob.RecurringId, job => job.ExecuteAsync(CancellationToken.None), "30 6 * * *", options);
    }

    /// <summary>
    /// Runs the listed recurring jobs once, now (configuration <c>Hangfire:TriggerOnStart</c>). A fresh Compose stack sets
    /// it to <see cref="IngestRecentReleasesJob.RecurringId"/> so tenders arrive without waiting for the next half hour.
    /// Ids that are not registered are skipped with a warning. Returns the ids that were triggered.
    /// </summary>
    public static IReadOnlyList<string> TriggerOnStart(IRecurringJobManager manager, IEnumerable<string> jobIds, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(jobIds);
        ArgumentNullException.ThrowIfNull(logger);
        var triggered = new List<string>();
        foreach (var id in jobIds.Select(i => i.Trim()).Where(i => i.Length > 0).Distinct(StringComparer.Ordinal))
        {
            if (!ExpectedRecurringJobIds.Contains(id, StringComparer.Ordinal))
            {
                logger.LogWarning("Hangfire:TriggerOnStart names {JobId}, which is not a recurring job; skipped", id);
                continue;
            }

            manager.Trigger(id);
            triggered.Add(id);
            logger.LogInformation("Triggered {JobId} on start (Hangfire:TriggerOnStart)", id);
        }

        return triggered;
    }

    /// <summary>Recurring job ids that must exist after <see cref="Register"/>; checked by tests and the readiness view.</summary>
    public static IReadOnlyList<string> ExpectedRecurringJobIds { get; } =
    [
        ProcessOutboxJob.RecurringId,
        IngestRecentReleasesJob.RecurringId,
        BackfillReleasesJob.RecurringId,
        SourceHealthCheckJob.RecurringId,
        LlmCostRollupJob.RecurringId,
        SweepNotificationsJob.RecurringId,
        DailyDigestJob.RecurringId,
        RefreshActiveTendersJob.RecurringId,
        ComputeStatusJob.RecurringId,
        ClosingRemindersJob.RecurringId,
        BriefingRemindersJob.RecurringId,
        DocumentExpiryCheckJob.RecurringId,
    ];
}
