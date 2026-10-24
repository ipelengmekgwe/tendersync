using System.ComponentModel;

namespace TenderSync.Application.Features.Classification;

/// <summary>Structured output of the classify prompt (PLAN.md §7.2 stage 2).</summary>
public sealed record ClassificationAnswer(
    [property: Description("Interest categories that apply, by code from the list given, with a confidence between 0 and 1.")] IReadOnlyList<CategoryConfidence> Categories,
    [property: Description("0 to 100: how relevant this tender is for an IT and software services company. 0 = nothing to do with IT; 100 = squarely a software or IT services contract.")] int Relevance,
    [property: Description("True when the work is mainly IT or software services (development, support, consulting, cloud, data, security), not hardware supply, licence resale or non-IT work.")] bool IsItServices,
    [property: Description("One to three sentences explaining the score, naming the evidence in the tender text.")] string Rationale,
    [property: Description("Reasons an IT services bidder might not qualify or want this: hardware-only, licence-only resale, OEM partnership required, local-presence rule, and similar. Empty when none.")] IReadOnlyList<string> RedFlags);

public sealed record CategoryConfidence(
    [property: Description("Category code exactly as listed, e.g. SW_DEV.")] string Code,
    [property: Description("0 to 1.")] double Confidence);
