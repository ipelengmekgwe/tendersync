using System.ComponentModel.DataAnnotations;

namespace TenderSync.Application.Features.Classification;

/// <summary>Weights and thresholds from PLAN.md §7.2, bound from the "Classification" section.</summary>
public sealed class ClassificationOptions
{
    public const string SectionName = "Classification";

    [Range(0, 10)]
    public int TitleWeight { get; set; } = 3;

    [Range(0, 10)]
    public int DescriptionWeight { get; set; } = 1;

    [Range(0, 10)]
    public int DocumentTitleWeight { get; set; } = 2;

    /// <summary>Each negative keyword hit (category or global) subtracts this.</summary>
    [Range(0, 10)]
    public int NegativeWeight { get; set; } = 2;

    /// <summary>A category with at least this score makes the tender a stage-2 candidate.</summary>
    [Range(1, 100)]
    public int CandidateMinScore { get; set; } = 1;

    /// <summary>Services or empty-category tenders with one of these procurement methods are candidates regardless of keywords.</summary>
    public string[] CandidateMethods { get; set; } = ["RFP", "RFI", "request for proposal", "request for information"];

    /// <summary>Stage-1 negatives that apply to every category (PLAN.md Appendix B).</summary>
    public string[] GlobalNegativeKeywords { get; set; } = ["ups", "generator", "sewer", "irrigation", "stadium", "borehole", "catering", "cleaning", "security guarding", "construction of"];

    /// <summary>"supply and delivery of" counts as a global negative unless one of these words follows it in the same sentence.</summary>
    public string[] SupplyAndDeliveryAllowList { get; set; } = ["software", "licence", "licences", "license", "licenses", "hardware", "laptop", "laptops", "computer", "computers", "server", "servers", "ict", "network", "switches"];

    /// <summary>Relevance at or above this creates a TenderMatch (default 60).</summary>
    [Range(1, 100)]
    public int MatchThreshold { get; set; } = 60;

    /// <summary>Relevance at or above this (and below the match threshold) is a near miss.</summary>
    [Range(0, 100)]
    public int NearMissThreshold { get; set; } = 40;

    /// <summary>Category codes the model returns with lower confidence than this are dropped.</summary>
    [Range(0, 1)]
    public double MinCategoryConfidence { get; set; } = 0.4;

    [Range(1, 100)]
    public int MaxDocumentTitles { get; set; } = 20;

    [Range(200, 20_000)]
    public int MaxDescriptionChars { get; set; } = 4000;
}
