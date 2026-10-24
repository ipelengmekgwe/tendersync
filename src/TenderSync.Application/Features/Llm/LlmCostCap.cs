using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Llm;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common;
using TenderSync.Domain.Operations;

namespace TenderSync.Application.Features.Llm;

/// <summary>
/// PLAN.md §8.4: token and cost caps per day. Spend is the sum of llm_calls for the current SAST day. Once reached, calls
/// throw <see cref="LlmCostCapExceededException"/> and one unhealthy source_health row ("llm-cost-cap") is written for the
/// day, which raises PipelineFailure through the outbox so operators are notified exactly once.
/// </summary>
public sealed class LlmCostCap(IAppDbContext db, IReadDb readDb, IOptions<LlmOptions> options, IDateTimeProvider clock, ILogger<LlmCostCap> logger)
{
    public const string HealthSource = "llm-cost-cap";

    public async Task<decimal> SpentTodayAsync(CancellationToken cancellationToken = default)
    {
        var dayStart = DayStartUtc(clock.UtcNow);
        return await readDb.ExecuteScalarAsync<decimal?>("select coalesce(sum(cost_usd), 0) from tendersync.llm_calls where at >= @dayStart", new { dayStart }, cancellationToken) ?? 0m;
    }

    /// <summary>Throws when the cap is reached. A cap of 0 disables the check.</summary>
    public async Task EnsureWithinCapAsync(string purpose, CancellationToken cancellationToken = default)
    {
        var cap = options.Value.DailyCostCapUsd;
        if (cap <= 0)
        {
            return;
        }

        var spent = await SpentTodayAsync(cancellationToken);
        if (spent < cap)
        {
            return;
        }

        await AlertOnceAsync(spent, cap, cancellationToken);
        logger.LogWarning("LLM call for {Purpose} blocked: ${Spent} spent today, cap ${Cap}", purpose, spent, cap);
        throw new LlmCostCapExceededException(string.Create(CultureInfo.InvariantCulture, $"Daily LLM cost cap reached: ${spent:F2} of ${cap:F2} spent today (SAST). Raise Llm:DailyCostCapUsd or wait for tomorrow."));
    }

    private async Task AlertOnceAsync(decimal spent, decimal cap, CancellationToken cancellationToken)
    {
        var dayStart = DayStartUtc(clock.UtcNow);
        var alreadyRaised = await readDb.ExecuteScalarAsync<long>(
            "select count(*) from tendersync.source_health where source = @source and ok = false and checked_at >= @dayStart",
            new { source = HealthSource, dayStart }, cancellationToken);
        if (alreadyRaised > 0)
        {
            return;
        }

        db.SourceHealthChecks.Add(SourceHealthCheck.Record(HealthSource, ok: false, httpStatus: null, latencyMs: null, schemaDriftJson: null,
            error: string.Create(CultureInfo.InvariantCulture, $"Daily LLM cost cap of ${cap:F2} reached (${spent:F2} spent). Model calls are paused until midnight SAST."), at: clock.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Midnight SAST of the current day, in UTC.</summary>
    public static DateTimeOffset DayStartUtc(DateTimeOffset utcNow)
    {
        var local = SouthAfricaTime.ToLocal(utcNow);
        return new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, local.Offset).ToUniversalTime();
    }
}
