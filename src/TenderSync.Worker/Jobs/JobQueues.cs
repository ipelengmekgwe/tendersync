namespace TenderSync.Worker.Jobs;

/// <summary>Hangfire queues from PLAN.md §13. Order matters: the server drains them in this priority.</summary>
public static class JobQueues
{
    public const string Notify = "notify";
    public const string Ingest = "ingest";
    public const string Pipeline = "pipeline";
    public const string Default = "default";

    public static string[] All { get; } = [Notify, Ingest, Pipeline, Default];

    /// <summary>Queues of the main server; the pipeline queue has a server of its own with a small worker count (PLAN.md §13).</summary>
    public static string[] Main { get; } = [Notify, Ingest, Default];
}
