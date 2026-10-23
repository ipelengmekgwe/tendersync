using Microsoft.Extensions.Logging;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Domain.Common;
using TenderSync.Domain.Operations;

namespace TenderSync.Application.Jobs;

/// <summary>What a job did, for the job_runs row.</summary>
public sealed record JobResult(int ItemsProcessed, object? Details = null)
{
    public static JobResult Nothing { get; } = new(0);
}

/// <summary>
/// Wraps a job body with a job_runs row: started, then completed or failed with the error. The start row is saved before
/// the body runs so a crash still leaves a trace (PLAN.md §6, pipeline health view). Failures are rethrown so Hangfire retries.
/// </summary>
public sealed class JobRunner(IAppDbContext db, IDateTimeProvider clock, ILogger<JobRunner> logger)
{
    public async Task<JobResult> RunAsync(string jobName, Func<CancellationToken, Task<JobResult>> body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);

        var run = JobRun.Start(jobName, clock.UtcNow);
        db.JobRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Job {JobName} started ({RunId})", jobName, run.Id);

        try
        {
            var result = await body(cancellationToken);
            run.Complete(result.ItemsProcessed, result.Details is null ? null : DomainJson.Serialize(result.Details), clock.UtcNow);
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogInformation("Job {JobName} completed: {Items} item(s)", jobName, result.ItemsProcessed);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Fail(ex.Message, 0, clock.UtcNow);
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogError(ex, "Job {JobName} failed", jobName);
            throw;
        }
    }
}
