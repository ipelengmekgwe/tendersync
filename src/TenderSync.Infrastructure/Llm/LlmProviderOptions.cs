using System.ComponentModel.DataAnnotations;

namespace TenderSync.Infrastructure.Llm;

/// <summary>Anthropic adapter settings (Llm:Anthropic). The key lives in user-secrets or the environment, never in appsettings.</summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Llm:Anthropic";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Override for proxies or gateways; empty uses api.anthropic.com.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    [Required]
    public string FastModel { get; set; } = "claude-haiku-5-5";

    [Required]
    public string StrongModel { get; set; } = "claude-opus-5-5";

    /// <summary>"default" asks for the model's server-side refusal fallback (PLAN.md §8.2); empty sends no fallbacks.</summary>
    public string Fallbacks { get; set; } = "default";

    /// <summary>anthropic-beta flags to send with every request.</summary>
    public string[] Betas { get; set; } = [];

    [Range(0, 10)]
    public int MaxRetries { get; set; } = 2;

    [Range(10, 3600)]
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>Cache the system prompt (stable prefix) with a 5-minute TTL.</summary>
    public bool CacheSystemPrompt { get; set; } = true;
}

/// <summary>Chat-completions adapter settings (Llm:OpenAiCompatible): Azure OpenAI, OpenRouter, a local Ollama, etc.</summary>
public sealed class OpenAiCompatibleOptions
{
    public const string SectionName = "Llm:OpenAiCompatible";

    /// <summary>Base URL including the version segment, e.g. https://api.openai.com/v1 or http://localhost:11434/v1.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string FastModel { get; set; } = string.Empty;

    public string StrongModel { get; set; } = string.Empty;

    /// <summary>Send response_format json_schema (set false for servers that only know json_object).</summary>
    public bool UseJsonSchema { get; set; } = true;

    [Range(10, 3600)]
    public int TimeoutSeconds { get; set; } = 300;
}
