using System.Diagnostics;
using TenderSync.Application.Abstractions.Llm;

namespace TenderSync.Application.Features.Llm;

/// <summary>
/// The <see cref="ILlmClient"/> every feature uses: checks the daily cap, routes the purpose to its provider, and records
/// an llm_calls row whether the call succeeds or fails. Provider adapters never write to the database themselves.
/// </summary>
public sealed class GuardedLlmClient(ILlmClientRouter router, LlmCostCap cap, ILlmCostRecorder recorder) : ILlmClient
{
    public string Provider => "routed";

    public async Task<LlmResult<T>> CompleteAsync<T>(LlmRequest request, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        await cap.EnsureWithinCapAsync(request.Purpose, cancellationToken);

        var client = router.Resolve(request.Purpose);
        var watch = Stopwatch.StartNew();
        try
        {
            var result = await client.CompleteAsync<T>(request, cancellationToken);
            await recorder.RecordAsync(new LlmCallRecord(request.Purpose, result.Provider, result.Model, request.PromptVersion, request.TenderId, result.Usage, result.CostUsd, result.Duration, true, null), CancellationToken.None);
            return result;
        }
        catch (LlmCallFailedException ex)
        {
            // The adapter got a response (refusal, bad JSON, truncation) and knows the usage; bill it.
            await recorder.RecordAsync(new LlmCallRecord(request.Purpose, client.Provider, ex.Model, request.PromptVersion, request.TenderId, ex.Usage, ex.CostUsd, watch.Elapsed, false, ex.Message), CancellationToken.None);
            throw ex.InnerException ?? ex;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await recorder.RecordAsync(new LlmCallRecord(request.Purpose, client.Provider, "unknown", request.PromptVersion, request.TenderId, new LlmUsage(0, 0, 0), 0m, watch.Elapsed, false, ex.Message), CancellationToken.None);
            throw;
        }
    }
}

/// <summary>
/// Thrown by adapters when a response arrived but could not be used, so the guard can record the tokens it cost.
/// The inner exception (refusal, output) is what callers see.
/// </summary>
public sealed class LlmCallFailedException(string model, LlmUsage usage, decimal costUsd, LlmException inner) : LlmException(inner.Message, inner)
{
    public string Model { get; } = model;

    public LlmUsage Usage { get; } = usage;

    public decimal CostUsd { get; } = costUsd;
}
