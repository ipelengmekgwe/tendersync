using Microsoft.Extensions.Logging;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Llm;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Domain.Operations;

namespace TenderSync.Application.Features.Llm;

/// <summary>Writes llm_calls rows. Saves immediately so the row survives a failing caller and feeds the daily cap at once.</summary>
public sealed class LlmCostRecorder(IAppDbContext db, IDateTimeProvider clock, ILogger<LlmCostRecorder> logger) : ILlmCostRecorder
{
    public async Task RecordAsync(LlmCallRecord call, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        db.LlmCalls.Add(LlmCall.Record(
            call.Purpose, call.Provider, call.Model, call.PromptVersion, call.TenderId,
            call.Usage.InputTokens, call.Usage.OutputTokens, call.Usage.CacheReadTokens,
            call.CostUsd, (int)Math.Min(int.MaxValue, call.Duration.TotalMilliseconds), call.Success, Truncate(call.Error), clock.UtcNow));
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "LLM {Purpose} via {Provider}/{Model} {Outcome}: {In} in, {Out} out, {CacheRead} cached, ${Cost} in {Ms} ms",
            call.Purpose, call.Provider, call.Model, call.Success ? "ok" : "failed", call.Usage.InputTokens, call.Usage.OutputTokens, call.Usage.CacheReadTokens, call.CostUsd, (int)call.Duration.TotalMilliseconds);
    }

    private static string? Truncate(string? error) => error is null || error.Length <= 2000 ? error : error[..2000];
}
