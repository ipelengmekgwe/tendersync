using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Llm;

namespace TenderSync.Application.Features.Llm;

/// <summary>
/// Turns usage into USD from the price table. Defaults are Anthropic list prices (Oct 2026, PLAN.md §7.8); configuration
/// under Llm:Pricing adds or overrides models (OpenAI-compatible endpoints must be configured, their prices are unknown here).
/// </summary>
public sealed class LlmPricing(IOptions<LlmOptions> options, ILogger<LlmPricing> logger)
{
    public static IReadOnlyDictionary<string, ModelPrice> Defaults { get; } = new Dictionary<string, ModelPrice>(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-haiku-5-5"] = new(0.10m, 0.50m, 0.01m),
        ["claude-sonnet-5-5"] = new(2m, 10m, 0.20m),
        ["claude-opus-5-5"] = new(4m, 20m, 0.20m),
        ["claude-opus-5"] = new(5m, 25m, 0.50m),
        ["claude-fable-5-1"] = new(10m, 50m, 0.25m),
    };

    private readonly HashSet<string> _warned = new(StringComparer.OrdinalIgnoreCase);

    public ModelPrice? PriceFor(string model)
    {
        if (options.Value.Pricing.TryGetValue(model, out var configured))
        {
            return configured;
        }

        var key = Defaults.Keys.FirstOrDefault(k => model.StartsWith(k, StringComparison.OrdinalIgnoreCase));
        return key is null ? null : Defaults[key];
    }

    /// <summary>Cost in USD, rounded to 6 decimals. Cache-write tokens are billed at 1.25 × input unless configured.</summary>
    public decimal Cost(string model, LlmUsage usage, int cacheWriteTokens = 0)
    {
        ArgumentNullException.ThrowIfNull(usage);
        var price = PriceFor(model);
        if (price is null)
        {
            lock (_warned)
            {
                if (_warned.Add(model))
                {
                    logger.LogWarning("No price configured for model {Model}; its calls are recorded at $0. Add Llm:Pricing:{Model}", model, model);
                }
            }

            return 0m;
        }

        var perToken = 1_000_000m;
        var cost = (usage.InputTokens * price.Input
                    + usage.OutputTokens * price.Output
                    + usage.CacheReadTokens * price.CacheRead
                    + cacheWriteTokens * (price.CacheWrite ?? price.Input * 1.25m)) / perToken;
        return Math.Round(cost, 6, MidpointRounding.AwayFromZero);
    }
}
