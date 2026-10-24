namespace TenderSync.Application.Abstractions.Llm;

/// <summary>Picks the provider adapter for a purpose (PLAN.md §8.2: Llm:Routing:{purpose} = anthropic | openai | fake).</summary>
public interface ILlmClientRouter
{
    ILlmClient Resolve(string purpose);

    IReadOnlyCollection<string> Providers { get; }
}

/// <summary>Writes one llm_calls row per model call, successful or not (PLAN.md §8.1).</summary>
public interface ILlmCostRecorder
{
    Task RecordAsync(LlmCallRecord call, CancellationToken cancellationToken = default);
}

public sealed record LlmCallRecord(
    string Purpose,
    string Provider,
    string Model,
    string? PromptVersion,
    Guid? TenderId,
    LlmUsage Usage,
    decimal CostUsd,
    TimeSpan Duration,
    bool Success,
    string? Error);

/// <summary>Versioned prompt files under Application/Prompts/{purpose}/v{n}.md (PLAN.md §8.3).</summary>
public interface IPromptRegistry
{
    /// <summary>The newest version of a purpose, or the given version.</summary>
    PromptTemplate Get(string purpose, int? version = null);

    IReadOnlyList<PromptTemplate> List();
}

/// <summary>A prompt file: the header fields and the body that becomes the system prompt.</summary>
public sealed record PromptTemplate(string Purpose, int Version, ModelTier Tier, string? SchemaName, string? Eval, string Body)
{
    public string VersionTag => $"v{Version}";
}

/// <summary>Base for failures the pipeline records on the llm_calls row and surfaces to the caller.</summary>
public class LlmException : Exception
{
    public LlmException()
    {
    }

    public LlmException(string message)
        : base(message)
    {
    }

    public LlmException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The provider declined the request for policy reasons (stop_reason "refusal") and no fallback answered.</summary>
public class LlmRefusalException : LlmException
{
    public LlmRefusalException()
    {
    }

    public LlmRefusalException(string message)
        : base(message)
    {
    }

    public LlmRefusalException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The model's text was not valid JSON for the requested type, or the output was truncated.</summary>
public class LlmOutputException : LlmException
{
    public LlmOutputException()
    {
    }

    public LlmOutputException(string message)
        : base(message)
    {
    }

    public LlmOutputException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Today's spend reached Llm:DailyCostCapUsd; the call was not made (PLAN.md §8.4).</summary>
public class LlmCostCapExceededException : LlmException
{
    public LlmCostCapExceededException()
    {
    }

    public LlmCostCapExceededException(string message)
        : base(message)
    {
    }

    public LlmCostCapExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
