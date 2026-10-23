using Hangfire;
using TenderSync.Application.Jobs;

namespace TenderSync.Worker.Jobs;

/// <summary>Drains the outbox every minute (PLAN.md §13). Idempotent; a second instance skips while one is running.</summary>
[Queue(JobQueues.Notify)]
[AutomaticRetry(Attempts = 3, DelaysInSeconds = [10, 30, 60])]
[DisableConcurrentExecution(timeoutInSeconds: 55)]
public sealed class ProcessOutboxJob(OutboxProcessor processor, JobRunner runner)
{
    public const string RecurringId = "process-outbox";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        runner.RunAsync(nameof(ProcessOutboxJob), async ct => new JobResult(await processor.ProcessAsync(OutboxProcessor.DefaultBatchSize, ct)), cancellationToken);
}
