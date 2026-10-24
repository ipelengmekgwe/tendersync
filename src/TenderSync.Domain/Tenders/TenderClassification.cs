using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Tenders;

/// <summary>Result of the two-stage classification for one tender (PLAN.md §7.2). Keyed by tender.</summary>
public sealed class TenderClassification
{
    private TenderClassification()
    {
    }

    public Guid TenderId { get; private set; }

    public ClassificationStage Stage { get; private set; }

    public string[] CategoryCodes { get; private set; } = [];

    /// <summary>0–100 relevance for an IT/software services company.</summary>
    public int Relevance { get; private set; }

    public string? Rationale { get; private set; }

    public string? Model { get; private set; }

    public string? PromptVersion { get; private set; }

    public int TokensIn { get; private set; }

    public int TokensOut { get; private set; }

    public DateTimeOffset ClassifiedAt { get; private set; }

    public static TenderClassification Keyword(Guid tenderId, IEnumerable<string> categoryCodes, int relevance, string? rationale, DateTimeOffset now)
    {
        return new TenderClassification
        {
            TenderId = tenderId,
            Stage = ClassificationStage.Keyword,
            CategoryCodes = [.. categoryCodes.Distinct(StringComparer.OrdinalIgnoreCase)],
            Relevance = Guard.InRange(relevance, 0, 100, nameof(relevance)),
            Rationale = rationale,
            ClassifiedAt = now,
        };
    }

    /// <summary>Re-runs of the keyword stage (tender text changed, categories edited) reset the LLM fields.</summary>
    public void ApplyKeywordResult(IEnumerable<string> categoryCodes, int relevance, string? rationale, DateTimeOffset now)
    {
        Stage = ClassificationStage.Keyword;
        CategoryCodes = [.. categoryCodes.Distinct(StringComparer.OrdinalIgnoreCase)];
        Relevance = Guard.InRange(relevance, 0, 100, nameof(relevance));
        Rationale = rationale;
        Model = null;
        PromptVersion = null;
        TokensIn = 0;
        TokensOut = 0;
        ClassifiedAt = now;
    }

    public void ApplyLlmResult(IEnumerable<string> categoryCodes, int relevance, string? rationale, string model, string promptVersion, int tokensIn, int tokensOut, DateTimeOffset now)
    {
        Stage = ClassificationStage.Llm;
        CategoryCodes = [.. categoryCodes.Distinct(StringComparer.OrdinalIgnoreCase)];
        Relevance = Guard.InRange(relevance, 0, 100, nameof(relevance));
        Rationale = rationale;
        Model = Guard.NotEmpty(model, nameof(model));
        PromptVersion = Guard.NotEmpty(promptVersion, nameof(promptVersion));
        TokensIn = tokensIn;
        TokensOut = tokensOut;
        ClassifiedAt = now;
    }
}
