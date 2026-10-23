using TenderSync.Domain.Common;

namespace TenderSync.Domain.Operations;

/// <summary>Cost and audit record of one model call (PLAN.md §8.1).</summary>
public sealed class LlmCall : Entity
{
    private LlmCall()
    {
    }

    public string Purpose { get; private set; } = null!;

    public string Provider { get; private set; } = null!;

    public string Model { get; private set; } = null!;

    public string? PromptVersion { get; private set; }

    public Guid? TenderId { get; private set; }

    public int TokensIn { get; private set; }

    public int TokensOut { get; private set; }

    public int CacheReadTokens { get; private set; }

    public decimal CostUsd { get; private set; }

    public int DurationMs { get; private set; }

    public bool Success { get; private set; }

    public string? Error { get; private set; }

    public DateTimeOffset At { get; private set; }

    public static LlmCall Record(string purpose, string provider, string model, string? promptVersion, Guid? tenderId, int tokensIn, int tokensOut, int cacheReadTokens, decimal costUsd, int durationMs, bool success, string? error, DateTimeOffset at)
    {
        return new LlmCall
        {
            Purpose = Guard.NotEmpty(purpose, nameof(purpose)),
            Provider = Guard.NotEmpty(provider, nameof(provider)),
            Model = Guard.NotEmpty(model, nameof(model)),
            PromptVersion = Guard.Trimmed(promptVersion),
            TenderId = tenderId,
            TokensIn = tokensIn,
            TokensOut = tokensOut,
            CacheReadTokens = cacheReadTokens,
            CostUsd = costUsd,
            DurationMs = durationMs,
            Success = success,
            Error = Guard.Trimmed(error),
            At = at,
        };
    }
}
