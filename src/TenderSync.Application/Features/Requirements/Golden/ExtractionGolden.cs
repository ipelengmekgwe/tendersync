using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Requirements.Golden;

/// <summary>
/// One file per tender under docs/golden/extraction/{ocid}.json: the human labels for the key fields of PLAN.md §7.4.
/// Null means "not in the bid pack" for scalar fields; an empty list means "none". Fill `labels`, leave `extracted` alone.
/// </summary>
public sealed record ExtractionLabels(
    DateTimeOffset? ClosingAt,
    DateTimeOffset? BriefingAt,
    bool? BriefingCompulsory,
    double? FunctionalityThreshold,
    string? EvaluationMethod,
    IReadOnlyList<RequirementType> MandatoryTypes,
    IReadOnlyList<string> RequiredFormCodes)
{
    public static ExtractionLabels Empty { get; } = new(null, null, null, null, null, [], []);
}

public sealed record ExtractionGoldenFile(string Ocid, string BidNumber, string Title, ExtractionLabels Labels, ExtractionLabels? Extracted, string? Note);

/// <summary>Per-field correctness for one tender and the aggregate the acceptance criterion reads (≥ 0.80 on the key fields).</summary>
public sealed record FieldScores(bool? ClosingAt, bool? BriefingAt, bool? BriefingCompulsory, bool? FunctionalityThreshold, bool? EvaluationMethod, double MandatoryTypesF1, double RequiredFormCodesF1)
{
    /// <summary>Mean over the fields the labels define (null fields in the labels are skipped), the two set fields counted by their F1.</summary>
    public double Accuracy
    {
        get
        {
            var values = new List<double>();
            foreach (var b in new[] { ClosingAt, BriefingAt, BriefingCompulsory, FunctionalityThreshold, EvaluationMethod })
            {
                if (b is not null)
                {
                    values.Add(b.Value ? 1 : 0);
                }
            }

            values.Add(MandatoryTypesF1);
            values.Add(RequiredFormCodesF1);
            return values.Count == 0 ? 0 : values.Average();
        }
    }
}

public static class ExtractionAccuracy
{
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    /// <summary>Scores the stored requirements against the labels. Scalar fields count only when the label is not null.</summary>
    public static FieldScores Score(ExtractionLabels labels, ExtractionLabels extracted)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(extracted);
        return new FieldScores(
            labels.ClosingAt is null ? null : SameMinute(labels.ClosingAt, extracted.ClosingAt),
            labels.BriefingAt is null ? null : SameMinute(labels.BriefingAt, extracted.BriefingAt),
            labels.BriefingCompulsory is null ? null : labels.BriefingCompulsory == extracted.BriefingCompulsory,
            labels.FunctionalityThreshold is null ? null : extracted.FunctionalityThreshold is { } t && Math.Abs(t - labels.FunctionalityThreshold.Value) < 0.01,
            labels.EvaluationMethod is null ? null : Normalise(labels.EvaluationMethod) == Normalise(extracted.EvaluationMethod),
            SetF1(labels.MandatoryTypes.Select(t => t.ToString()), extracted.MandatoryTypes.Select(t => t.ToString())),
            SetF1(labels.RequiredFormCodes.Select(Normalise), extracted.RequiredFormCodes.Select(Normalise)));
    }

    /// <summary>Projects stored rows and tender columns onto the label shape so they can be compared.</summary>
    public static ExtractionLabels FromStored(Tender tender, IReadOnlyList<TenderRequirement> rows)
    {
        ArgumentNullException.ThrowIfNull(tender);
        ArgumentNullException.ThrowIfNull(rows);
        var evaluation = rows.FirstOrDefault(r => r.Kind == RequirementKind.Evaluation);
        var submission = rows.FirstOrDefault(r => r.Kind == RequirementKind.Submission);
        return new ExtractionLabels(
            ReadDate(submission?.ValueJson, "closingAt") ?? tender.ClosingAt,
            tender.BriefingAt,
            tender.BriefingCompulsory,
            (double?)evaluation?.Threshold,
            ReadString(evaluation?.ValueJson, "evaluationMethod"),
            rows.Where(r => r.Kind == RequirementKind.Mandatory).Select(r => r.Type).Distinct().ToList(),
            rows.Where(r => r.Kind == RequirementKind.Document).Select(r => ReadString(r.ValueJson, "formCode")).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    public static double SetF1(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        var e = expected.Where(s => s.Length > 0).ToHashSet(StringComparer.Ordinal);
        var a = actual.Where(s => s.Length > 0).ToHashSet(StringComparer.Ordinal);
        if (e.Count == 0 && a.Count == 0)
        {
            return 1;
        }

        var tp = e.Intersect(a, StringComparer.Ordinal).Count();
        if (tp == 0)
        {
            return 0;
        }

        var precision = (double)tp / a.Count;
        var recall = (double)tp / e.Count;
        return 2 * precision * recall / (precision + recall);
    }

    public static string Normalise(string? value) => value is null ? string.Empty : new string(value.ToLower(CultureInfo.InvariantCulture).Where(char.IsLetterOrDigit).ToArray());

    private static bool SameMinute(DateTimeOffset? expected, DateTimeOffset? actual) =>
        expected is not null && actual is not null && Math.Abs((expected.Value.ToUniversalTime() - actual.Value.ToUniversalTime()).TotalMinutes) < 1;

    private static string? ReadString(string? json, string property)
    {
        if (json is null)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static DateTimeOffset? ReadDate(string? json, string property) => RequirementExtractor.ParseDate(ReadString(json, property));
}
