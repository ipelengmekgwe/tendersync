namespace TenderSync.Application.Abstractions.Llm;

/// <summary>
/// Provider-neutral model access (PLAN.md §8.1). Implemented by the Anthropic and OpenAI-compatible adapters in WP06.
/// Every call returns typed JSON validated against the schema of <typeparamref name="T"/>, plus citations and usage.
/// </summary>
public interface ILlmClient
{
    string Provider { get; }

    Task<LlmResult<T>> CompleteAsync<T>(LlmRequest request, CancellationToken cancellationToken = default)
        where T : class;
}

public enum ModelTier
{
    /// <summary>Cheap and quick: classification, fuzzy matching.</summary>
    Fast = 0,

    /// <summary>Most capable: requirement extraction, qualification reasoning.</summary>
    Strong = 1,
}

public enum LlmEffort
{
    Low = 0,
    Medium = 1,
    High = 2,
}

public abstract record LlmPart;

public sealed record LlmTextPart(string Text) : LlmPart;

/// <summary>A document to reason over. PDFs are sent natively where the provider supports it; otherwise as extracted text.</summary>
public sealed record LlmDocumentPart(ReadOnlyMemory<byte> Content, string MediaType, string Title, string? ExtractedText = null) : LlmPart;

public sealed record LlmRequest(
    string Purpose,
    string PromptVersion,
    string SystemPrompt,
    IReadOnlyList<LlmPart> UserParts,
    ModelTier Tier = ModelTier.Fast,
    LlmEffort Effort = LlmEffort.Medium,
    int MaxOutputTokens = 4096,
    bool EnableCitations = false,
    Guid? TenderId = null);

public sealed record LlmCitation(int DocumentIndex, int? StartPage, int? EndPage, string CitedText);

public sealed record LlmUsage(int InputTokens, int OutputTokens, int CacheReadTokens);

public sealed record LlmResult<T>(
    T Value,
    string RawText,
    IReadOnlyList<LlmCitation> Citations,
    LlmUsage Usage,
    decimal CostUsd,
    string Provider,
    string Model,
    TimeSpan Duration)
    where T : class;
