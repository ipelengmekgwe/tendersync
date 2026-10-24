using Hangfire;
using Hangfire.Common;
using Hangfire.Server;
using TenderSync.Worker.Jobs;

namespace TenderSync.Worker.Infrastructure;

/// <summary>
/// Starts the Hangfire servers when the host starts, reading configuration at that moment (not at registration) so test
/// hosts can turn the servers off with Hangfire:ServerEnabled=false after Program.cs has run. Two servers (PLAN.md §13):
/// the main one drains notify, ingest and default; the pipeline one runs the document and LLM steps with a small worker
/// count so they keep to the polite concurrency.
/// </summary>
internal sealed class HangfireServersHostedService(IServiceProvider provider, IConfiguration configuration, JobStorage storage, ILogger<HangfireServersHostedService> logger) : IHostedService, IDisposable
{
    private readonly List<BackgroundJobServer> _servers = [];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Hangfire:ServerEnabled", true))
        {
            logger.LogInformation("Hangfire servers are disabled (Hangfire:ServerEnabled=false); jobs stay queued");
            return Task.CompletedTask;
        }

        _servers.Add(Start("worker", JobQueues.Main, configuration.GetValue("Hangfire:WorkerCount", Math.Max(2, Environment.ProcessorCount))));
        _servers.Add(Start("pipeline", [JobQueues.Pipeline], configuration.GetValue("Hangfire:PipelineWorkerCount", 2)));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var server in _servers)
        {
            server.SendStop();
        }

        foreach (var server in _servers)
        {
            await server.WaitForShutdownAsync(cancellationToken);
        }
    }

    public void Dispose()
    {
        foreach (var server in _servers)
        {
            server.Dispose();
        }

        _servers.Clear();
    }

    private BackgroundJobServer Start(string name, string[] queues, int workerCount)
    {
        var options = new BackgroundJobServerOptions
        {
            ServerName = $"{Environment.MachineName}:{name}",
            Queues = queues,
            WorkerCount = workerCount,
            Activator = provider.GetService<JobActivator>(),
            FilterProvider = provider.GetService<IJobFilterProvider>(),
            TimeZoneResolver = provider.GetService<ITimeZoneResolver>(),
        };

        logger.LogInformation("Starting Hangfire server {Server} for queues {Queues} with {Workers} worker(s)", options.ServerName, string.Join(",", queues), workerCount);
        return new BackgroundJobServer(options, storage, provider.GetServices<IBackgroundProcess>());
    }
}
