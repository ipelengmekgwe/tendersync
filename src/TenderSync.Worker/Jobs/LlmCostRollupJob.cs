using Hangfire;
using MediatR;
using Microsoft.Extensions.Options;
using TenderSync.Application.Features.Admin.Queries.GetLlmCallsSummary;
using TenderSync.Application.Features.Llm;
using TenderSync.Application.Jobs;

namespace TenderSync.Worker.Jobs;

/// <summary>
/// Daily 23:55 SAST (PLAN.md §13): records the day's model spend per purpose and model in job_runs so the pipeline
/// health view and the dashboard have one row per day, and warns when spend passed 80% of the cap.
/// </summary>
[Queue(JobQueues.Pipeline)]
[AutomaticRetry(Attempts = 1)]
public sealed class LlmCostRollupJob(ISender sender, IOptions<LlmOptions> options, JobRunner runner, ILogger<LlmCostRollupJob> logger)
{
    public const string RecurringId = "llm-cost-rollup";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        runner.RunAsync(nameof(LlmCostRollupJob), async ct =>
        {
            var dayStart = LlmCostCap.DayStartUtc(DateTimeOffset.UtcNow);
            var summary = await sender.Send(new GetLlmCallsSummaryQuery(dayStart, dayStart.AddDays(1), Recent: 0), ct);
            var cap = options.Value.DailyCostCapUsd;
            if (cap > 0 && summary.CostUsd >= cap * 0.8m)
            {
                logger.LogWarning("LLM spend today is ${Spent} of the ${Cap} cap", summary.CostUsd, cap);
            }

            return new JobResult(summary.Calls, new
            {
                day = dayStart,
                summary.Calls,
                summary.Failed,
                summary.TokensIn,
                summary.TokensOut,
                summary.CacheRead,
                summary.CostUsd,
                capUsd = cap,
                byPurpose = summary.ByPurpose,
                byModel = summary.ByModel,
            });
        }, cancellationToken);
}
