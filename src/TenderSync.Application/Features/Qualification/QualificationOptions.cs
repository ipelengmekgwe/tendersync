using System.ComponentModel.DataAnnotations;
using TenderSync.Application.Abstractions.Llm;
using TenderSync.Domain.Qualification;

namespace TenderSync.Application.Features.Qualification;

/// <summary>Weights and model settings for the qualification engine (PLAN.md §7.5), bound from the "Qualification" section.</summary>
public sealed class QualificationOptions
{
    public const string SectionName = "Qualification";

    /// <summary>Score weights (PLAN.md §7.5 step 4). Only their ratio matters.</summary>
    [Range(0, 1000)]
    public decimal MandatoryWeight { get; set; } = 60;

    [Range(0, 1000)]
    public decimal FunctionalityWeight { get; set; } = 30;

    [Range(0, 1000)]
    public decimal DocumentsWeight { get; set; } = 10;

    /// <summary>A model-matched certification counts as Pass from this confidence; below it the item stays Unknown.</summary>
    [Range(0, 1)]
    public double FuzzyMatchMinConfidence { get; set; } = 0.7;

    public ModelTier EstimateTier { get; set; } = ModelTier.Strong;

    public LlmEffort EstimateEffort { get; set; } = LlmEffort.Medium;

    [Range(1024, 64_000)]
    public int EstimateMaxOutputTokens { get; set; } = 6000;

    [Range(256, 16_000)]
    public int FuzzyMaxOutputTokens { get; set; } = 2048;

    /// <summary>Reference projects sent to the model, newest first.</summary>
    [Range(1, 100)]
    public int MaxReferenceProjects { get; set; } = 15;

    public QualificationWeights Weights => new(MandatoryWeight, FunctionalityWeight, DocumentsWeight);
}
