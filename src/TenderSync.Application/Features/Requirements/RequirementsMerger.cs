using System.Globalization;
using System.Text.RegularExpressions;
using TenderSync.Application.Abstractions.Llm;

namespace TenderSync.Application.Features.Requirements;

/// <summary>One document's extraction with the document it came from and the model's citations (native PDF path).</summary>
public sealed record DocumentExtraction(Guid DocumentId, string DocumentTitle, TenderRequirementsV1 Result, IReadOnlyList<LlmCitation> Citations);

/// <summary>A section value with the document it was read from.</summary>
public sealed record Sourced<T>(Guid DocumentId, T Value);

/// <summary>The per-tender result after merging the documents (PLAN.md §7.4: main bid document wins conflicts).</summary>
public sealed record MergedRequirements(
    string? Summary,
    IReadOnlyList<Sourced<MandatoryRequirementV1>> Mandatory,
    IReadOnlyList<Sourced<FunctionalCriterionV1>> Functional,
    Sourced<double>? FunctionalityThreshold,
    Sourced<string>? EvaluationMethod,
    IReadOnlyList<Sourced<RequiredDocumentV1>> RequiredDocuments,
    Sourced<BriefingV1>? Briefing,
    Sourced<SubmissionV1>? Submission,
    Sourced<PricingV1>? Pricing,
    Sourced<ContractV1>? Contract,
    Sourced<ContactsV1>? Contacts);

/// <summary>
/// Merges per-document extractions: the main document (first in the list) wins for single-valued sections; list items
/// are unioned with duplicates dropped by type and normalised text, keeping the higher-confidence copy. Before merging,
/// items without a page get one from the citation whose cited text overlaps their quote.
/// </summary>
public static partial class RequirementsMerger
{
    public static MergedRequirements Merge(IReadOnlyList<DocumentExtraction> extractions)
    {
        ArgumentNullException.ThrowIfNull(extractions);
        var docs = extractions.Select(AttributePages).ToList();

        return new MergedRequirements(
            First(docs, d => string.IsNullOrWhiteSpace(d.Result.Summary) ? null : d.Result.Summary.Trim())?.Value,
            Union(docs, d => d.Result.Mandatory, m => $"{m.Type}|{Key(m.Text)}", m => m.Confidence),
            Union(docs, d => d.Result.Functional, f => Key(f.Criterion), f => f.Confidence),
            FirstValue(docs, d => d.Result.FunctionalityThreshold),
            First(docs, d => string.IsNullOrWhiteSpace(d.Result.EvaluationMethod) ? null : d.Result.EvaluationMethod.Trim()),
            Union(docs, d => d.Result.RequiredDocuments, r => string.IsNullOrWhiteSpace(r.FormCode) ? Key(r.Name) : $"form:{Key(r.FormCode)}", r => r.Confidence),
            First(docs, d => d.Result.Briefing),
            First(docs, d => d.Result.Submission),
            First(docs, d => d.Result.Pricing),
            First(docs, d => d.Result.Contract),
            First(docs, d => d.Result.Contacts is { } c && (c.EnquiriesEmail is not null || c.EnquiriesPhone is not null) ? c : null));
    }

    /// <summary>Fills null pages from citations whose cited text overlaps the item's quote (native PDF path).</summary>
    public static DocumentExtraction AttributePages(DocumentExtraction extraction)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        if (extraction.Citations.Count == 0)
        {
            return extraction;
        }

        var r = extraction.Result;
        var result = r with
        {
            Mandatory = r.Mandatory.Select(m => m with { Source = Attribute(m.Source, extraction.Citations) }).ToList(),
            Functional = r.Functional.Select(f => f with { Source = Attribute(f.Source, extraction.Citations) }).ToList(),
            RequiredDocuments = r.RequiredDocuments.Select(d => d with { Source = Attribute(d.Source, extraction.Citations) }).ToList(),
            Briefing = r.Briefing is null ? null : r.Briefing with { Source = Attribute(r.Briefing.Source, extraction.Citations) },
            Submission = r.Submission is null ? null : r.Submission with { Source = Attribute(r.Submission.Source, extraction.Citations) },
            Pricing = r.Pricing is null ? null : r.Pricing with { Source = Attribute(r.Pricing.Source, extraction.Citations) },
            Contract = r.Contract is null ? null : r.Contract with { Source = Attribute(r.Contract.Source, extraction.Citations) },
        };
        return extraction with { Result = result };
    }

    public static RequirementSourceV1 Attribute(RequirementSourceV1? source, IReadOnlyList<LlmCitation> citations)
    {
        source ??= new RequirementSourceV1(null, null);
        if (source.Page is not null || string.IsNullOrWhiteSpace(source.Quote))
        {
            return source;
        }

        var quote = Key(source.Quote);
        var probe = quote.Length > 40 ? quote[..40] : quote;
        foreach (var citation in citations)
        {
            if (citation.StartPage is null || string.IsNullOrWhiteSpace(citation.CitedText))
            {
                continue;
            }

            var cited = Key(citation.CitedText);
            if (cited.Contains(probe, StringComparison.Ordinal) || quote.Contains(cited.Length > 40 ? cited[..40] : cited, StringComparison.Ordinal))
            {
                return source with { Page = citation.StartPage };
            }
        }

        return source;
    }

    /// <summary>Lower-case, whitespace-collapsed, punctuation-stripped comparison key.</summary>
    public static string Key(string? text) => text is null ? string.Empty : Collapse().Replace(Punctuation().Replace(text.ToLower(CultureInfo.InvariantCulture), " "), " ").Trim();

    private static Sourced<T>? First<T>(IEnumerable<DocumentExtraction> docs, Func<DocumentExtraction, T?> pick)
    {
        foreach (var doc in docs)
        {
            var value = pick(doc);
            if (value is not null)
            {
                return new Sourced<T>(doc.DocumentId, value);
            }
        }

        return null;
    }

    private static Sourced<T>? FirstValue<T>(IEnumerable<DocumentExtraction> docs, Func<DocumentExtraction, T?> pick)
        where T : struct
    {
        foreach (var doc in docs)
        {
            var value = pick(doc);
            if (value is not null)
            {
                return new Sourced<T>(doc.DocumentId, value.Value);
            }
        }

        return null;
    }

    private static List<Sourced<T>> Union<T>(IEnumerable<DocumentExtraction> docs, Func<DocumentExtraction, IReadOnlyList<T>> items, Func<T, string> key, Func<T, double> confidence)
    {
        var best = new Dictionary<string, Sourced<T>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var doc in docs)
        {
            foreach (var item in items(doc) ?? [])
            {
                var k = key(item);
                if (k.Length == 0)
                {
                    continue;
                }

                if (!best.TryGetValue(k, out var existing))
                {
                    best[k] = new Sourced<T>(doc.DocumentId, item);
                    order.Add(k);
                }
                else if (confidence(item) > confidence(existing.Value))
                {
                    best[k] = new Sourced<T>(doc.DocumentId, item);
                }
            }
        }

        return order.Select(k => best[k]).ToList();
    }

    [GeneratedRegex(@"[^\p{L}\p{N}\s]", RegexOptions.CultureInvariant)]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Collapse();
}
