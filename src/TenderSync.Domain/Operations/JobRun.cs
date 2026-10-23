using TenderSync.Domain.Common;

namespace TenderSync.Domain.Operations;

/// <summary>One execution of a background job, for the pipeline health view.</summary>
public sealed class JobRun : Entity
{
    private JobRun()
    {
    }

    public string JobName { get; private set; } = null!;

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public bool? Success { get; private set; }

    public int ItemsProcessed { get; private set; }

    public string? Error { get; private set; }

    public string? DetailsJson { get; private set; }

    public static JobRun Start(string jobName, DateTimeOffset now) =>
        new() { JobName = Guard.NotEmpty(jobName, nameof(jobName)), StartedAt = now };

    public void Complete(int itemsProcessed, string? detailsJson, DateTimeOffset now)
    {
        ItemsProcessed = itemsProcessed;
        DetailsJson = Guard.Trimmed(detailsJson);
        FinishedAt = now;
        Success = true;
        Error = null;
    }

    public void Fail(string error, int itemsProcessed, DateTimeOffset now)
    {
        ItemsProcessed = itemsProcessed;
        FinishedAt = now;
        Success = false;
        Error = Guard.NotEmpty(error, nameof(error));
    }
}
