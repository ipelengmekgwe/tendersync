using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Domain.Qualification;

/// <summary>What an evaluator may look at: the requirement, the company with its documents, the closing date and the clock.</summary>
public sealed record EvaluationContext(
    TenderRequirement Requirement,
    CompanyProfile Profile,
    IReadOnlyList<CompanyDocument> Documents,
    DateTimeOffset? ClosingAt,
    DateTimeOffset Now)
{
    /// <summary>Compliance must still hold when the bid closes (PLAN.md §7.5: csd_expiry &gt; closing_at), or now when the closing date is past or unknown.</summary>
    public DateTimeOffset ValidityPoint => ClosingAt is { } closing && closing > Now ? closing : Now;

    /// <summary>The requirement's threshold as the document wrote it ("level 4", "R10 million"), from the value JSON.</summary>
    public string? ValueText => RequirementValueParser.ValueText(Requirement);
}

/// <summary>
/// Outcome of one evaluator. <see cref="MissingDocument"/> names the company document whose absence caused a Fail;
/// <see cref="NeedsFuzzyMatch"/> asks the qualifier to let the model compare the requirement with the profile's certifications.
/// </summary>
public sealed record RequirementEvaluation(
    FindingOutcome Outcome,
    string Detail,
    decimal Confidence,
    CompanyDocumentType? MissingDocument = null,
    bool NeedsFuzzyMatch = false)
{
    public static RequirementEvaluation Pass(string detail, decimal confidence = 0.95m) => new(FindingOutcome.Pass, detail, confidence);

    public static RequirementEvaluation Fail(string detail, CompanyDocumentType? missingDocument = null, decimal confidence = 0.95m) => new(FindingOutcome.Fail, detail, confidence, missingDocument);

    public static RequirementEvaluation Expired(string detail, CompanyDocumentType? missingDocument = null) => new(FindingOutcome.Expired, detail, 0.95m, missingDocument);

    public static RequirementEvaluation Unknown(string detail, bool needsFuzzyMatch = false) => new(FindingOutcome.Unknown, detail, 0.5m, null, needsFuzzyMatch);
}

/// <summary>Deterministic check of one requirement type against the company profile (PLAN.md §7.5 step 1).</summary>
public interface IRequirementEvaluator
{
    RequirementType Type { get; }

    RequirementEvaluation Evaluate(EvaluationContext context);
}
