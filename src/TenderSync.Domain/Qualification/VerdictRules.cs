using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Qualification;

/// <summary>Relative weights of the three score components (PLAN.md §7.5 step 4: "weighted combination, configurable").</summary>
public sealed record QualificationWeights(decimal Mandatory = 60, decimal Functionality = 30, decimal Documents = 10)
{
    public static QualificationWeights Default { get; } = new();
}

/// <summary>
/// PLAN.md §7.5 step 4. Any mandatory Fail → NotQualify. Any Unknown or Expired, or an estimated functionality score under the
/// threshold → Partial. All Pass and the estimate at or above the threshold → Qualify. Expired is Partial rather than
/// NotQualify because a renewal before closing fixes it; the report lists it as a question.
/// </summary>
public static class VerdictRules
{
    public static QualificationVerdict Decide(IReadOnlyList<QualificationFinding> mandatory, int? estimatedFunctionality, decimal? functionalityThreshold)
    {
        ArgumentNullException.ThrowIfNull(mandatory);
        if (mandatory.Any(f => f.Outcome == FindingOutcome.Fail))
        {
            return QualificationVerdict.NotQualify;
        }

        if (mandatory.Any(f => f.Outcome is FindingOutcome.Unknown or FindingOutcome.Expired))
        {
            return QualificationVerdict.Partial;
        }

        if (functionalityThreshold is { } threshold && (estimatedFunctionality is null || estimatedFunctionality < threshold))
        {
            return QualificationVerdict.Partial;
        }

        return QualificationVerdict.Qualify;
    }

    /// <summary>
    /// 0–100: mandatory share (Pass = 1, Unknown = ½, Expired = ¼, Fail = 0; 1 when there are none), the functionality estimate
    /// out of 100 (½ when there is nothing to estimate), and the document share (Present = 1, Expired = ½, Missing = 0; forms
    /// and unmapped items excluded; 1 when there are none), combined by the weights.
    /// </summary>
    public static int Score(IReadOnlyList<QualificationFinding> mandatory, int? estimatedFunctionality, IReadOnlyList<DocumentCheck> documents, QualificationWeights weights)
    {
        ArgumentNullException.ThrowIfNull(mandatory);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(weights);

        var mandatoryShare = mandatory.Count == 0 ? 1m : mandatory.Sum(f => f.Outcome switch
        {
            FindingOutcome.Pass => 1m,
            FindingOutcome.Unknown => 0.5m,
            FindingOutcome.Expired => 0.25m,
            _ => 0m,
        }) / mandatory.Count;

        var functionalityShare = estimatedFunctionality is { } estimate ? Math.Clamp(estimate, 0, 100) / 100m : 0.5m;

        var scored = documents.Where(d => d.Outcome is DocumentOutcome.Present or DocumentOutcome.Missing or DocumentOutcome.Expired).ToList();
        var documentShare = scored.Count == 0 ? 1m : scored.Sum(d => d.Outcome switch
        {
            DocumentOutcome.Present => 1m,
            DocumentOutcome.Expired => 0.5m,
            _ => 0m,
        }) / scored.Count;

        var totalWeight = weights.Mandatory + weights.Functionality + weights.Documents;
        if (totalWeight <= 0)
        {
            return 0;
        }

        var score = (weights.Mandatory * mandatoryShare + weights.Functionality * functionalityShare + weights.Documents * documentShare) / totalWeight * 100m;
        return (int)Math.Clamp(Math.Round(score, MidpointRounding.AwayFromZero), 0, 100);
    }
}
