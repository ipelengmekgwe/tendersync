using Hangfire;
using TenderSync.Application.Features.Notifications;
using TenderSync.Application.Jobs;

namespace TenderSync.Worker.Jobs;

/// <summary>Delivers one notification row (PLAN.md §13: SendNotification, enqueued per message). Channel failures are recorded on the row with a backoff, never thrown.</summary>
[Queue(JobQueues.Notify)]
[AutomaticRetry(Attempts = 2, DelaysInSeconds = [30, 120])]
public sealed class SendNotificationJob(NotificationSender sender, JobRunner runner)
{
    public Task ExecuteAsync(Guid notificationId, CancellationToken cancellationToken) =>
        runner.RunAsync(nameof(SendNotificationJob), async ct =>
        {
            var outcome = await sender.SendAsync(notificationId, ct);
            return new JobResult(outcome.Attempted ? 1 : 0, outcome);
        }, cancellationToken);
}

/// <summary>
/// Every minute: sends rows that are due but were not delivered by their own job, i.e. rows held by quiet hours or a retry
/// backoff, and rows created by the API (POST /notifications/test), which cannot enqueue jobs.
/// </summary>
[Queue(JobQueues.Notify)]
[AutomaticRetry(Attempts = 0)]
[DisableConcurrentExecution(timeoutInSeconds: 55)]
public sealed class SweepNotificationsJob(NotificationSender sender, JobRunner runner)
{
    public const string RecurringId = "sweep-notifications";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        runner.RunAsync(nameof(SweepNotificationsJob), async ct => new JobResult(await sender.SendDueAsync(ct)), cancellationToken);
}

/// <summary>07:00 SAST digest (PLAN.md §13), delivered only to preferences that opted into DailyDigest.</summary>
[Queue(JobQueues.Notify)]
[AutomaticRetry(Attempts = 1, DelaysInSeconds = [300])]
public sealed class DailyDigestJob(DailyDigestBuilder digest, JobRunner runner)
{
    public const string RecurringId = "daily-digest";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        runner.RunAsync(nameof(DailyDigestJob), async ct =>
        {
            var outcome = await digest.SendAsync(ct);
            return new JobResult(outcome.Dispatch.Created, outcome);
        }, cancellationToken);
}
