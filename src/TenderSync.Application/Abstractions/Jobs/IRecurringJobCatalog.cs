namespace TenderSync.Application.Abstractions.Jobs;

/// <summary>The recurring jobs the Worker has registered, read from the job store so the API can list them without running a job server.</summary>
public interface IRecurringJobCatalog
{
    Task<IReadOnlyList<RecurringJobInfo>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>One recurring job: its cron (in <see cref="TimeZone"/>), queue, next and last execution, and the state of the last run it started.</summary>
public sealed record RecurringJobInfo(
    string Id,
    string Cron,
    string? TimeZone,
    string? Queue,
    DateTimeOffset? NextExecution,
    DateTimeOffset? LastExecution,
    string? LastJobState,
    string? Error);
