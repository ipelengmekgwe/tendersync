using Hangfire;
using TenderSync.Application.Features.Classification;
using TenderSync.Application.Jobs;

namespace TenderSync.Worker.Jobs;

/// <summary>Two-stage classification of one tender (PLAN.md §7.2), enqueued for every new or changed tender.</summary>
[Queue(JobQueues.Pipeline)]
[AutomaticRetry(Attempts = 2, DelaysInSeconds = [120, 900])]
public sealed class ClassifyTenderJob(TenderClassifier classifier, JobRunner runner)
{
    public Task ExecuteAsync(Guid tenderId, CancellationToken cancellationToken) =>
        runner.RunAsync(nameof(ClassifyTenderJob), async ct =>
        {
            var outcome = await classifier.ClassifyAsync(tenderId, prefilterOnly: false, ct);
            return new JobResult(1, outcome);
        }, cancellationToken);
}
