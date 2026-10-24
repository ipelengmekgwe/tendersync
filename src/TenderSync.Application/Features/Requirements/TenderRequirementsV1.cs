using System.ComponentModel;
using TenderSync.Domain.Enums;

namespace TenderSync.Application.Features.Requirements;

/// <summary>Structured output of the extract-requirements prompt for one document (PLAN.md §7.4).</summary>
public sealed record TenderRequirementsV1(
    [property: Description("Two to four plain-language sentences on what is being procured, for whom and for how long. Empty string if this document says nothing about scope.")] string Summary,
    [property: Description("Mandatory (pass/fail) requirements a bidder must meet. Empty list if none in this document.")] IReadOnlyList<MandatoryRequirementV1> Mandatory,
    [property: Description("Functionality or technical evaluation criteria with their weights. Empty list if none.")] IReadOnlyList<FunctionalCriterionV1> Functional,
    [property: Description("Minimum functionality score to proceed to price evaluation, as a number out of 100 (70 for '70 out of 100' or '70%'), or null.")] double? FunctionalityThreshold,
    [property: Description("Evaluation method as written: '80/20', '90/10', 'functionality then price', or null.")] string? EvaluationMethod,
    [property: Description("Documents and forms the bidder must submit. Empty list if none.")] IReadOnlyList<RequiredDocumentV1> RequiredDocuments,
    [property: Description("Briefing or site meeting, or null when this document mentions none.")] BriefingV1? Briefing,
    [property: Description("How and where bids are submitted, or null.")] SubmissionV1? Submission,
    [property: Description("Pricing rules, or null.")] PricingV1? Pricing,
    [property: Description("Contract term, or null.")] ContractV1? Contract,
    [property: Description("Enquiry contact details, or null.")] ContactsV1? Contacts);

public sealed record RequirementSourceV1(
    [property: Description("1-based page number of this document where the text appears, or null if unknown.")] int? Page,
    [property: Description("Exact quote from the document supporting the item, under 300 characters, or null.")] string? Quote);

public sealed record MandatoryRequirementV1(
    [property: Description("Closest type from the list; Other when none fits.")] RequirementType Type,
    [property: Description("The requirement as the document states it, condensed to one or two sentences.")] string Text,
    [property: Description("The threshold or detail as written: 'level 4', 'R5 million', '3 references over R1m in the last 5 years', or null.")] string? Value,
    RequirementSourceV1 Source,
    [property: Description("0 to 1: how sure you are that this is a mandatory requirement with this value.")] double Confidence);

public sealed record FunctionalCriterionV1(
    [property: Description("The criterion as written.")] string Criterion,
    [property: Description("Weight or points as a number (30 for '30 points' or '30%'), or null.")] double? Weight,
    [property: Description("Minimum score for this criterion if the document sets one, or null.")] double? Threshold,
    [property: Description("How points are awarded, condensed, or null.")] string? ScoringNotes,
    RequirementSourceV1 Source,
    double Confidence);

public sealed record RequiredDocumentV1(
    [property: Description("Name of the document or form as written.")] string Name,
    [property: Description("Form code if any: SBD1, SBD4, SBD6.1, MBD4, MBD6.1, etc.; otherwise null.")] string? FormCode,
    [property: Description("True when the bid is disqualified without it.")] bool Mandatory,
    [property: Description("Conditions such as 'certified copy', 'not older than 3 months', or null.")] string? Notes,
    RequirementSourceV1 Source,
    double Confidence);

public sealed record BriefingV1(
    [property: Description("Date and time of the briefing in ISO 8601 with the +02:00 offset (2026-11-03T10:00:00+02:00), or null.")] string? DateIso,
    [property: Description("True when attendance is compulsory, false when optional, null when not stated.")] bool? Compulsory,
    [property: Description("Venue as written, or null.")] string? Venue,
    [property: Description("Link for a virtual briefing, or null.")] string? VirtualLink,
    RequirementSourceV1 Source,
    double Confidence);

public sealed record SubmissionV1(
    [property: Description("ETendersOnline for the eTenders portal, Physical for a tender box or hand delivery, Email, Other, or Unknown.")] SubmissionMethod Method,
    [property: Description("Physical address, email address or portal as written, or null.")] string? Address,
    [property: Description("Closing date and time in ISO 8601 with the +02:00 offset, or null.")] string? ClosingAtIso,
    [property: Description("Format rules: copies, sealed envelopes, file formats, labelling, or null.")] string? FormatRules,
    RequirementSourceV1 Source,
    double Confidence);

public enum PricingBasis
{
    Unknown = 0,
    FixedPrice = 1,
    TimeAndMaterials = 2,
    RateCard = 3,
}

public sealed record PricingV1(
    PricingBasis Basis,
    [property: Description("Currency code, usually ZAR, or null.")] string? Currency,
    [property: Description("VAT rule as written ('prices inclusive of VAT'), or null.")] string? VatRule,
    [property: Description("Bid validity period in days, or null.")] int? ValidityDays,
    [property: Description("Line items of the pricing schedule if listed, else empty.")] IReadOnlyList<string> ScheduleItems,
    RequirementSourceV1 Source,
    double Confidence);

public sealed record ContractV1(
    [property: Description("Contract duration in months, or null.")] int? DurationMonths,
    [property: Description("Start date or trigger as written, or null.")] string? Start,
    [property: Description("Extension or renewal options as written, or null.")] string? ExtensionOptions,
    RequirementSourceV1 Source,
    double Confidence);

public sealed record ContactsV1(
    [property: Description("Email address for enquiries, or null.")] string? EnquiriesEmail,
    [property: Description("Phone number for enquiries, or null.")] string? EnquiriesPhone);
