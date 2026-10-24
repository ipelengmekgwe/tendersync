using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TenderSync.Domain.Categories;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Classification;

/// <summary>The fields the prefilter and the LLM see. Built from a tender or from the dry-run endpoint's input.</summary>
public sealed record TenderText(
    string BidNumber,
    string Title,
    string Description,
    IReadOnlyList<string> DocumentTitles,
    ProcurementCategory MainCategory,
    string? ProcurementMethodDetails,
    string? BuyerName = null)
{
    public static TenderText From(Tender tender, int maxDocumentTitles = 20)
    {
        ArgumentNullException.ThrowIfNull(tender);
        return new TenderText(
            tender.BidNumber, tender.Title, tender.Description,
            tender.Documents.OrderBy(d => d.DatePublished).Select(d => d.Title).Distinct(StringComparer.OrdinalIgnoreCase).Take(maxDocumentTitles).ToList(),
            tender.MainCategory, tender.ProcurementMethodDetails, tender.Buyer?.Name);
    }
}

public sealed record KeywordHit(string Keyword, string Field, int Weight);

public sealed record CategoryScore(string Code, int Score, IReadOnlyList<KeywordHit> Hits, IReadOnlyList<KeywordHit> NegativeHits);

public sealed record PrefilterResult(bool IsCandidate, string Reason, IReadOnlyList<CategoryScore> Scores, IReadOnlyList<string> GlobalNegativeHits, bool MethodRuleApplied)
{
    /// <summary>Categories that scored at or above the candidate threshold, best first.</summary>
    public IReadOnlyList<string> CandidateCodes { get; init; } = [];
}

/// <summary>
/// Stage 1 of PLAN.md §7.2, no LLM: per enabled category, keyword hits weighted by field (title ×3, description ×1,
/// document titles ×2) minus negative hits ×2. Matching is whole-word and case-insensitive so "ups" never matches
/// "groups" and "api" never matches "capital". A recall filter: it decides who reaches the model, not who matches.
/// </summary>
public sealed class KeywordPrefilter(IOptions<ClassificationOptions> options)
{
    private static readonly ConcurrentDictionary<string, Regex> Patterns = new(StringComparer.Ordinal);

    public PrefilterResult Evaluate(TenderText text, IEnumerable<InterestCategory> categories)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(categories);
        var settings = options.Value;

        var fields = new (string Name, string Text, int Weight)[]
        {
            ("title", text.Title ?? string.Empty, settings.TitleWeight),
            ("description", text.Description ?? string.Empty, settings.DescriptionWeight),
            ("documents", string.Join("\n", text.DocumentTitles ?? []), settings.DocumentTitleWeight),
        };
        var everything = string.Join("\n", fields.Select(f => f.Text));

        var globalNegatives = settings.GlobalNegativeKeywords.Where(k => Matches(k, everything)).ToList();
        if (SupplyAndDeliveryIsNegative(everything, settings.SupplyAndDeliveryAllowList))
        {
            globalNegatives.Add("supply and delivery of");
        }

        var scores = new List<CategoryScore>();
        foreach (var category in categories.Where(c => c.Enabled))
        {
            var hits = new List<KeywordHit>();
            foreach (var keyword in category.Keywords)
            {
                foreach (var (name, fieldText, weight) in fields)
                {
                    if (weight > 0 && Matches(keyword, fieldText))
                    {
                        hits.Add(new KeywordHit(keyword, name, weight * category.Weight));
                    }
                }
            }

            var negatives = category.NegativeKeywords.Where(k => Matches(k, everything)).Select(k => new KeywordHit(k, "any", -settings.NegativeWeight))
                .Concat(globalNegatives.Select(k => new KeywordHit(k, "global", -settings.NegativeWeight)))
                .ToList();

            var score = hits.Sum(h => h.Weight) + negatives.Sum(h => h.Weight);
            if (hits.Count > 0 || negatives.Count > 0)
            {
                scores.Add(new CategoryScore(category.Code, score, hits, negatives));
            }
        }

        scores.Sort((a, b) => b.Score.CompareTo(a.Score));
        var candidates = scores.Where(s => s.Score >= settings.CandidateMinScore).Select(s => s.Code).ToList();

        var methodRule = candidates.Count == 0
            && text.MainCategory is ProcurementCategory.Services or ProcurementCategory.Unknown
            && !string.IsNullOrWhiteSpace(text.ProcurementMethodDetails)
            && settings.CandidateMethods.Any(m => Matches(m, text.ProcurementMethodDetails))
            && globalNegatives.Count == 0;

        var isCandidate = candidates.Count > 0 || methodRule;
        var reason = candidates.Count > 0
            ? $"Keyword hits: {string.Join(", ", scores.Where(s => s.Score >= settings.CandidateMinScore).Select(s => $"{s.Code}={s.Score}"))}"
            : methodRule
                ? $"No keyword match, but {text.ProcurementMethodDetails} for a services/unspecified tender goes to the model"
                : globalNegatives.Count > 0
                    ? $"Not IT: negative keywords ({string.Join(", ", globalNegatives.Distinct(StringComparer.Ordinal))})"
                    : "No interest-category keywords in title, description or document titles";

        return new PrefilterResult(isCandidate, reason, scores, globalNegatives, methodRule) { CandidateCodes = candidates };
    }

    /// <summary>Whole-word, case-insensitive containment; letters and digits on either side of the phrase block a match.</summary>
    public static bool Matches(string keyword, string text)
    {
        if (string.IsNullOrWhiteSpace(keyword) || string.IsNullOrEmpty(text))
        {
            return false;
        }

        // Optional plural on the last word: "software licence" also matches "software licences", "system" matches "systems".
        var pattern = Patterns.GetOrAdd(keyword.Trim(), k => new Regex(
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(k).Replace(@"\ ", @"\s+", StringComparison.Ordinal)}(?:e?s)?(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
            TimeSpan.FromSeconds(1)));
        return pattern.IsMatch(text);
    }

    private static bool SupplyAndDeliveryIsNegative(string text, string[] allowList)
    {
        foreach (Match match in Regex.Matches(text, @"supply(,| and| &)?\s+(delivery|deliver)\s+of", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            var sentenceEnd = text.IndexOfAny(['.', '\n'], match.Index);
            var rest = text[match.Index..(sentenceEnd < 0 ? text.Length : sentenceEnd)];
            if (!allowList.Any(word => Matches(word, rest)))
            {
                return true;
            }
        }

        return false;
    }
}
