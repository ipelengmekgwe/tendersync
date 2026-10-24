using System.ComponentModel.DataAnnotations;
using TenderSync.Application.Abstractions.Llm;

namespace TenderSync.Application.Features.Requirements;

/// <summary>Limits for requirement extraction (PLAN.md §7.4), bound from the "Extraction" section.</summary>
public sealed class ExtractionOptions
{
    public const string SectionName = "Extraction";

    /// <summary>PDFs up to this size go to the model natively with citations; larger ones go as text with page markers.</summary>
    [Range(1024, 100L * 1024 * 1024)]
    public long MaxNativePdfBytes { get; set; } = 32L * 1024 * 1024;

    [Range(1, 2000)]
    public int MaxNativePdfPages { get; set; } = 600;

    /// <summary>Set false to always send extracted text (cheaper, no citations).</summary>
    public bool PreferNativePdf { get; set; } = true;

    [Range(1, 50)]
    public int MaxDocumentsPerTender { get; set; } = 8;

    [Range(1000, 2_000_000)]
    public int MaxTextCharsPerDocument { get; set; } = 300_000;

    [Range(1024, 64_000)]
    public int MaxOutputTokens { get; set; } = 8192;

    public ModelTier Tier { get; set; } = ModelTier.Strong;

    public LlmEffort Effort { get; set; } = LlmEffort.Medium;

    /// <summary>A tender costing more than this is logged as a warning (acceptance: ≤ $1 average).</summary>
    [Range(0, 1000)]
    public decimal WarnCostPerTenderUsd { get; set; } = 1m;

    /// <summary>Document titles matching one of these words are treated as the main bid document (first processed, wins conflicts).</summary>
    public string[] MainDocumentWords { get; set; } = ["bid document", "tender document", "terms of reference", "request for proposal", "request for quotation", "rfp", "rfq", "invitation to bid", "specification", "bid pack"];
}
