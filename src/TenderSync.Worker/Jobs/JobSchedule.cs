using Hangfire;
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
