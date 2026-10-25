using System.ComponentModel.DataAnnotations;

namespace TenderSync.Application.Features.Llm;

/// <summary>Provider-neutral model settings (PLAN.md §8): routing per purpose, the daily cost cap and the price table.</summary>
public sealed class LlmOptions
{
    public const string SectionName = "Llm";
    public const string AnthropicProvider = "anthropic";
    public const string OpenAiProvider = "openai";
    public const string FakeProvider = "fake";

    /// <summary>Provider for purposes without a routing entry.</summary>
    [Required]
    public string DefaultProvider { get; set; } = AnthropicProvider;

    /// <summary>Purpose → provider name, e.g. "classify": "anthropic".</summary>
    public Dictionary<string, string> Routing { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Calls stop for the rest of the day once llm_calls sums to this (USD). 0 disables the cap.</summary>
    [Range(0, 100_000)]
    public decimal DailyCostCapUsd { get; set; } = 10m;

    /// <summary>PLAN.md §8.4: company documents may be sent to the provider. Default true for Anthropic; set false for unknown OpenAI-compatible endpoints.</summary>
    public bool AllowCompanyDocuments { get; set; } = true;

    /// <summary>
    /// Purpose → JSON answer the fake provider returns, so a local stack can run a purpose without a key (route it with
    /// Llm:Routing:{purpose} = fake). Meant for Development and e2e runs; every call returns the same answer.
    /// </summary>
    public Dictionary<string, string> FakeAnswers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Folder for RecordingLlmClient fixtures. Empty disables recording.</summary>
    public string RecordingPath { get; set; } = string.Empty;

    /// <summary>USD per million tokens by model id. Missing models cost 0 and are logged once.</summary>
    public Dictionary<string, ModelPrice> Pricing { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string ProviderFor(string purpose) =>
        Routing.TryGetValue(purpose, out var provider) && !string.IsNullOrWhiteSpace(provider) ? provider.Trim().ToLowerInvariant() : DefaultProvider.Trim().ToLowerInvariant();
}

/// <summary>Prices per million tokens. Cache writes default to 1.25 × input (5-minute TTL).</summary>
public sealed class ModelPrice
{
    public decimal Input { get; set; }

    public decimal Output { get; set; }

    public decimal CacheRead { get; set; }

    public decimal? CacheWrite { get; set; }

    public ModelPrice()
    {
    }

    public ModelPrice(decimal input, decimal output, decimal cacheRead, decimal? cacheWrite = null)
    {
        Input = input;
        Output = output;
        CacheRead = cacheRead;
        CacheWrite = cacheWrite;
    }
}
