using System.ComponentModel;

namespace TenderSync.Application.Features.Llm;

/// <summary>Answer schema of the "smoke" prompt: proves structured output and page citations against a real bid document.</summary>
public sealed record SmokeSummary(
    [property: Description("One sentence on what is being procured.")] string Summary,
    [property: Description("The bid or tender number exactly as printed, or null.")] string? BidNumber,
    [property: Description("The closing date and time exactly as printed, or null.")] string? ClosingDate,
    [property: Description("1-based page number where the closing date appears, or null.")] int? ClosingDatePage);
