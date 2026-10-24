using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Domain.Enums;

namespace TenderSync.Application.Features.Classification.Golden;

/// <summary>
/// One line of docs/golden/classification.jsonl. <see cref="IsItMatch"/> is the human label (null until reviewed);
/// the prefilter fields are the machine's suggestion at build time, kept so reviewers see what stage 1 thought.
/// </summary>
public sealed record GoldenRow(
    string Ocid,
    string BidNumber,
    string Title,
    string Description,
    string BuyerName,
    ProcurementCategory MainCategory,
    string? ProcurementMethodDetails,
    IReadOnlyList<string> DocumentTitles,
    bool PrefilterCandidate,
    IReadOnlyList<string> PrefilterCodes,
    string PrefilterReason,
    bool? IsItMatch,
    IReadOnlyList<string>? Categories,
    string? Note)
{
    public TenderText ToText() => new(BidNumber, Title, Description, DocumentTitles, MainCategory, ProcurementMethodDetails, BuyerName);
}

/// <summary>Precision and recall for a binary label.</summary>
public sealed record EvalMetrics(int TruePositives, int FalsePositives, int FalseNegatives, int TrueNegatives)
{
    public int Total => TruePositives + FalsePositives + FalseNegatives + TrueNegatives;

    public double Precision => TruePositives + FalsePositives == 0 ? 0 : (double)TruePositives / (TruePositives + FalsePositives);

    public double Recall => TruePositives + FalseNegatives == 0 ? 0 : (double)TruePositives / (TruePositives + FalseNegatives);

    public double F1 => Precision + Recall == 0 ? 0 : 2 * Precision * Recall / (Precision + Recall);

    public static EvalMetrics From(IEnumerable<(bool Predicted, bool Actual)> pairs)
    {
        int tp = 0, fp = 0, fn = 0, tn = 0;
        foreach (var (predicted, actual) in pairs)
        {
            switch (predicted, actual)
            {
                case (true, true): tp++; break;
                case (true, false): fp++; break;
                case (false, true): fn++; break;
                default: tn++; break;
            }
        }

        return new EvalMetrics(tp, fp, fn, tn);
    }

    public override string ToString() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"n={Total} tp={TruePositives} fp={FalsePositives} fn={FalseNegatives} tn={TrueNegatives} precision={Precision:F3} recall={Recall:F3} f1={F1:F3}");
}

/// <summary>Reads and writes the JSONL file; camelCase, one row per line, labels preserved on rebuild.</summary>
public static class GoldenSetFile
{
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() }, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public static IReadOnlyList<GoldenRow> Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        return File.ReadLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<GoldenRow>(line, Json) ?? throw new InvalidDataException("Empty golden row."))
            .ToList();
    }

    public static void Write(string path, IEnumerable<GoldenRow> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllLines(path, rows.Select(r => JsonSerializer.Serialize(r, Json)));
    }
}

/// <summary>
/// Builds the review file from the database (PLAN.md §7.2): every prefilter candidate plus a spread of non-candidates
/// so the reviewed set measures both recall and precision. Existing labels and notes are kept by ocid.
/// </summary>
public sealed class GoldenSetBuilder(IReadDb readDb, IAppDbContext db, KeywordPrefilter prefilter, IOptions<ClassificationOptions> options)
{
    private const string Sql = """
        select t.ocid, t.bid_number, t.title, t.description, b.name as buyer_name, t.main_category, t.procurement_method_details,
               coalesce((select array_agg(d.title order by d.date_published) from tendersync.tender_documents d where d.tender_id = t.id), '{}') as document_titles
        from tendersync.tenders t
        join tendersync.buyers b on b.id = t.buyer_id
        where t.published_at >= @since
        order by t.published_at desc, t.ocid
        """;

    public async Task<GoldenBuildSummary> BuildAsync(string path, int days, int targetRows, CancellationToken cancellationToken = default)
    {
        var categories = await db.InterestCategories.ListAsync(enabledOnly: true, cancellationToken);
        var existing = GoldenSetFile.Read(path).ToDictionary(r => r.Ocid, StringComparer.Ordinal);
        var rows = await readDb.QueryAsync<Row>(Sql, new { since = DateTimeOffset.UtcNow.AddDays(-days) }, cancellationToken);

        var candidates = new List<GoldenRow>();
        var others = new List<GoldenRow>();
        foreach (var row in rows)
        {
            var text = new TenderText(row.BidNumber, row.Title, row.Description, row.DocumentTitles, (ProcurementCategory)row.MainCategory, row.ProcurementMethodDetails, row.BuyerName);
            var result = prefilter.Evaluate(text, categories);
            existing.TryGetValue(row.Ocid, out var previous);
            var golden = new GoldenRow(
                row.Ocid, row.BidNumber, row.Title, Truncate(row.Description, options.Value.MaxDescriptionChars), row.BuyerName, (ProcurementCategory)row.MainCategory, row.ProcurementMethodDetails,
                row.DocumentTitles, result.IsCandidate, result.CandidateCodes, result.Reason, previous?.IsItMatch, previous?.Categories, previous?.Note);
            (result.IsCandidate ? candidates : others).Add(golden);
        }

        // Keep every labelled row, every candidate, then spread the remaining budget evenly over the non-candidates.
        var selected = candidates.ToList();
        var labelledOthers = others.Where(o => o.IsItMatch is not null).ToList();
        selected.AddRange(labelledOthers);
        var unlabelledOthers = others.Where(o => o.IsItMatch is null).ToList();
        var wanted = Math.Max(targetRows - selected.Count, unlabelledOthers.Count / 4);
        if (wanted > 0 && unlabelledOthers.Count > 0)
        {
            var step = Math.Max(1, unlabelledOthers.Count / Math.Min(wanted, unlabelledOthers.Count));
            selected.AddRange(unlabelledOthers.Where((_, index) => index % step == 0).Take(wanted));
        }

        var ordered = selected.OrderByDescending(r => r.PrefilterCandidate).ThenBy(r => r.Ocid, StringComparer.Ordinal).ToList();
        GoldenSetFile.Write(path, ordered);
        return new GoldenBuildSummary(path, rows.Count, candidates.Count, ordered.Count, ordered.Count(r => r.IsItMatch is not null));
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    private sealed class Row
    {
        public string Ocid { get; init; } = string.Empty;

        public string BidNumber { get; init; } = string.Empty;

        public string Title { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public string BuyerName { get; init; } = string.Empty;

        public int MainCategory { get; init; }

        public string? ProcurementMethodDetails { get; init; }

        public string[] DocumentTitles { get; init; } = [];
    }
}

public sealed record GoldenBuildSummary(string Path, int TendersScanned, int PrefilterCandidates, int RowsWritten, int Labelled);
