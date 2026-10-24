using Hangfire;
using TenderSync.Application.Features.Status;
using TenderSync.Application.Jobs;

namespace TenderSync.Worker.Jobs;

/// <summary>Daily 05:00 SAST (PLAN.md §13): re-fetches every matched, unfinished tender by ocid so changes, awards and cancellations are seen the same morning.</summary>
[Queue(JobQueues.Ingest)]
[AutomaticRetry(Attempts = 1, DelaysInSeconds = [600])]
[DisableConcurrentExecution(timeoutInSeconds: 2 * 60 * 60)]
public sealed class RefreshActiveTendersJob(TenderStatusRefresher refresher, JobRunner runner)
{
    public const string RecurringId = "refresh-active-tenders";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        runner.RunAsync(nameof(RefreshActiveTendersJob), async ct =>
        {
            var summary = await refresher.RefreshActiveAsync(ct);
            return new JobResult(summary.Candidates, summary);
        }, cancellationToken);
}

/// <summary>Hourly (PLAN.md §13): Open → ClosingSoon → Closed as time passes, Awarded once awards appear.</summary>
[Queue(JobQueues.Pipeline)]
[AutomaticRetry(Attempts = 0)]
[DisableConcurrentExecution(timeoutInSeconds: 50 * 60)]
public sealed class ComputeStatusJob(StatusComputer computer, JobRunner runner)
{
    public const string RecurringId = "compute-status";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        runner.RunAsync(nameof(ComputeStatusJob), async ct =>
        {
            var summary = await computer.ComputeAsync(ct);
            return new JobResult(summary.Transitioned, summary);
        }, cancellationToken);
}

/// <summary>Daily 06:00 SAST: 7/3/1-day closing reminders for matched, non-dismissed tenders.</summary>
[Queue(JobQueues.Notify)]
[AutomaticRetry(Attempts = 1, DelaysInSeconds = [300])]
[DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
public sealed class ClosingRemindersJob(ReminderService reminders, JobRunner runner)
{
    public const string RecurringId = "closing-reminders";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        runner.RunAsync(nameof(ClosingRemindersJob), async ct =>
        {
            var summary = await reminders.SendClosingRemindersAsync(ct);
            return new JobResult(summary.Created, summary);
        }, cancellationToken);
}

/// <summary>Daily 06:00 SAST: briefing reminders two days ahead (compulsory briefings also for dismissed matches).</summary>
[Queue(JobQueues.Notify)]
[AutomaticRetry(Attempts = 1, DelaysInSeconds = [300])]
[DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
public sealed class BriefingRemindersJob(ReminderService reminders, JobRunner runner)
{
    public const string RecurringId = "briefing-reminders";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        runner.RunAsync(nameof(BriefingRemindersJob), async ct =>
        {
            var summary = await reminders.SendBriefingRemindersAsync(ct);
            return new JobResult(summary.Created, summary);
        }, cancellationToken);
}

/// <summary>Daily 06:30 SAST: company documents expiring within thirty days.</summary>
[Queue(JobQueues.Notify)]
[AutomaticRetry(Attempts = 1, DelaysInSeconds = [300])]
[DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
public sealed class DocumentExpiryCheckJob(ReminderService reminders, JobRunner runner)
{
    public const string RecurringId = "document-expiry-check";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        runner.RunAsync(nameof(DocumentExpiryCheckJob), async ct =>
        {
            var summary = await reminders.SendDocumentExpiryAsync(ct);
            return new JobResult(summary.Created, summary);
        }, cancellationToken);
}
