using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Tenders;

/// <summary>One requirement extracted from a bid pack, with its source citation and confidence (PLAN.md §7.4).</summary>
public sealed class TenderRequirement : Entity
{
    public const decimal VerifyBelowConfidence = 0.5m;

    private TenderRequirement()
    {
    }

    public Guid TenderId { get; private set; }

    public RequirementKind Kind { get; private set; }

    public RequirementType Type { get; private set; }

    public string Text { get; private set; } = null!;

    /// <summary>Structured value for the type (e.g. minimum B-BBEE level) as JSON.</summary>
    public string? ValueJson { get; private set; }

    public bool Mandatory { get; private set; }

    public decimal? Weight { get; private set; }

    public decimal? Threshold { get; private set; }

    public RequirementSource? Source { get; private set; }

    public decimal Confidence { get; private set; }

    public string Model { get; private set; } = null!;

    public string PromptVersion { get; private set; } = null!;

    public DateTimeOffset ExtractedAt { get; private set; }

    public bool NeedsVerification => Confidence < VerifyBelowConfidence || Source is null || Source.Page is null;

    public static TenderRequirement Create(
        Guid tenderId,
        RequirementKind kind,
        RequirementType type,
        string text,
        bool mandatory,
        decimal confidence,
        RequirementSource? source,
        string model,
        string promptVersion,
        DateTimeOffset now,
        string? valueJson = null,
        decimal? weight = null,
        decimal? threshold = null)
    {
        if (confidence is < 0 or > 1)
        {
            throw new DomainException("Confidence must be between 0 and 1.");
        }

        return new TenderRequirement
        {
            TenderId = tenderId,
            Kind = kind,
            Type = type,
            Text = Guard.NotEmpty(text, nameof(text)),
            Mandatory = mandatory,
            Confidence = confidence,
            Source = source,
            Model = Guard.NotEmpty(model, nameof(model)),
            PromptVersion = Guard.NotEmpty(promptVersion, nameof(promptVersion)),
            ExtractedAt = now,
            ValueJson = valueJson,
            Weight = weight,
            Threshold = threshold,
        };
    }
}
