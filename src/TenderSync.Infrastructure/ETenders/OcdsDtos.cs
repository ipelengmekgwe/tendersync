using System.Text.Json;
using System.Text.Json.Serialization;

namespace TenderSync.Infrastructure.ETenders;

/// <summary>
/// OCDS 1.1 release package as the eTenders API serves it (verified shape in PLAN.md Appendix A). Dates and amounts are kept
/// loosely typed because the feed sends empty strings and string numbers; <see cref="ReleaseMapper"/> parses them tolerantly.
/// </summary>
public sealed class OcdsReleasePackage
{
    [JsonPropertyName("uri")]
    public string? Uri { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("publishedDate")]
    public string? PublishedDate { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("releases")]
    public List<JsonElement> Releases { get; set; } = [];

    [JsonPropertyName("links")]
    public OcdsLinks? Links { get; set; }
}

public sealed class OcdsLinks
{
    [JsonPropertyName("next")]
    public string? Next { get; set; }

    [JsonPropertyName("prev")]
    public string? Prev { get; set; }
}

public sealed class OcdsRelease
{
    [JsonPropertyName("ocid")]
    public string? Ocid { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("tag")]
    public List<string> Tag { get; set; } = [];

    [JsonPropertyName("initiationType")]
    public string? InitiationType { get; set; }

    [JsonPropertyName("tender")]
    public OcdsTender? Tender { get; set; }

    [JsonPropertyName("buyer")]
    public OcdsOrganizationReference? Buyer { get; set; }

    [JsonPropertyName("awards")]
    public List<JsonElement> Awards { get; set; } = [];

    [JsonPropertyName("contracts")]
    public List<JsonElement> Contracts { get; set; } = [];
}

public sealed class OcdsTender
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The buyer's bid number, not a title (PLAN.md §2).</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("mainProcurementCategory")]
    public string? MainProcurementCategory { get; set; }

    [JsonPropertyName("additionalProcurementCategories")]
    public List<string> AdditionalProcurementCategories { get; set; } = [];

    /// <summary>The human title and body.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("value")]
    public OcdsValue? Value { get; set; }

    [JsonPropertyName("documents")]
    public List<OcdsDocument> Documents { get; set; } = [];

    [JsonPropertyName("tenderPeriod")]
    public OcdsPeriod? TenderPeriod { get; set; }

    [JsonPropertyName("procuringEntity")]
    public OcdsOrganizationReference? ProcuringEntity { get; set; }

    [JsonPropertyName("procurementMethod")]
    public string? ProcurementMethod { get; set; }

    [JsonPropertyName("procurementMethodDetails")]
    public string? ProcurementMethodDetails { get; set; }
}

public sealed class OcdsValue
{
    [JsonPropertyName("amount")]
    public JsonElement? Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }
}

public sealed class OcdsPeriod
{
    [JsonPropertyName("startDate")]
    public string? StartDate { get; set; }

    [JsonPropertyName("endDate")]
    public string? EndDate { get; set; }
}

public sealed class OcdsOrganizationReference
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class OcdsDocument
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("documentType")]
    public string? DocumentType { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("datePublished")]
    public string? DatePublished { get; set; }

    [JsonPropertyName("dateModified")]
    public string? DateModified { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }
}

/// <summary>A release together with its raw JSON, kept verbatim in tender_releases.</summary>
public sealed record OcdsReleaseEnvelope(OcdsRelease Release, string RawJson);

/// <summary>One page of the releases endpoint.</summary>
public sealed record OcdsPage(int PageNumber, string RawJson, IReadOnlyList<OcdsReleaseEnvelope> Releases, string? NextUrl);
