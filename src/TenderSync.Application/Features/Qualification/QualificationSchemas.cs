using System.ComponentModel;

namespace TenderSync.Application.Features.Qualification;

/// <summary>Structured output of the estimate-functionality prompt (PLAN.md §7.5 step 3).</summary>
public sealed record FunctionalityEstimateV1(
    [property: Description("One entry per criterion, in the order the criteria were given.")] IReadOnlyList<CriterionEstimateV1> Criteria,
    [property: Description("Two to four sentences on the company's overall strengths and gaps against these criteria.")] string OverallAssessment,
    [property: Description("Facts the bidder must confirm or supply before this estimate can be trusted. Empty list if none.")] IReadOnlyList<string> QuestionsForBidder);

public sealed record CriterionEstimateV1(
    [property: Description("The criterion text exactly as given.")] string Criterion,
    [property: Description("Maximum points for the criterion as given, or 100 when the criterion had no weight.")] double MaxPoints,
    [property: Description("Points the company would most likely score, between 0 and maxPoints.")] double EstimatedPoints,
    [property: Description("One or two sentences naming the company facts the estimate rests on, or the evidence that is missing.")] string Reasoning,
    [property: Description("0 to 1: how well the company summary covers the evidence an evaluator would want for this criterion.")] double Confidence);

/// <summary>Structured output of the fuzzy-match-certification prompt (PLAN.md §7.5 step 1, Certification).</summary>
public sealed record CertificationMatchV1(
    [property: Description("One entry per requirement, in the order the requirements were given.")] IReadOnlyList<CertificationMatchItemV1> Matches);

public sealed record CertificationMatchItemV1(
    [property: Description("Index of the requirement in the list given, starting at 0.")] int RequirementIndex,
    [property: Description("The company certification that satisfies the requirement, copied exactly from the list, or null when none does.")] string? MatchedCertification,
    [property: Description("0 to 1: how sure you are that the certification satisfies the requirement as written. 0 when nothing matches.")] double Confidence,
    [property: Description("One sentence: why it satisfies the requirement, or what is missing.")] string Reasoning);
