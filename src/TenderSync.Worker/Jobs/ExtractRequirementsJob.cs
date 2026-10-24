using Hangfire;
using TenderSync.Application.Features.Requirements;
using TenderSync.Application.Jobs;

namespace TenderSync.Worker.Jobs;

/// <summary>Requirement extraction for one tender (PLAN.md §7.4): one model call per usable document, merged and persisted.</summary>
[Queue(JobQueues.Pipeline)]
[AutomaticRetry(Attempts = 1, DelaysInSeconds = [600])]
public sealed class ExtractRequirementsJob(RequirementExtractor extractor, JobRunner runner)
{
    public Task ExecuteAsync(Guid tenderId, CancellationToken cancellationToken) =>
        runner.RunAsync(nameof(ExtractRequirementsJob), async ct =>
        {
            var outcome = await extractor.ExtractAsync(tenderId, ct);
            return new JobResult(outcome.Requirements, outcome);
        }, cancellationToken);
}
