using Hangfire;
using TenderSync.Application.Features.Qualification;
using TenderSync.Application.Jobs;

namespace TenderSync.Worker.Jobs;

/// <summary>Qualification of one tender for one or every matched company profile (PLAN.md §7.5), chained after requirement extraction.</summary>
[Queue(JobQueues.Pipeline)]
[AutomaticRetry(Attempts = 1, DelaysInSeconds = [600])]
public sealed class QualifyTenderJob(TenderQualifier qualifier, JobRunner runner)
{
    public Task ExecuteAsync(Guid tenderId, Guid? companyProfileId, CancellationToken cancellationToken) =>
        runner.RunAsync(nameof(QualifyTenderJob), async ct =>
        {
            var outcome = await qualifier.QualifyAsync(tenderId, companyProfileId, ct);
            return new JobResult(outcome.Profiles.Count, outcome);
        }, cancellationToken);
}
