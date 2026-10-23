namespace TenderSync.Worker.Jobs;

/// <summary>Hangfire queues from PLAN.md §13. Order matters: the server drains them in this priority.</summary>
public static class JobQueues
{
    public const string Notify = "notify";
    public const string Ingest = "ingest";
    public const string Pipeline = "pipeline";
    public const string Default = "default";

    public static string[] All { get; } = [Notify, Ingest, Pipeline, Default];
}
