using System.Text.RegularExpressions;

namespace TenderSync.Domain.Tenders;

/// <summary>
/// Cancellation patterns (PLAN.md §7.7): the eTenders feed never sets tender.status, so a cancelled tender shows up as a
/// release whose human title, description or a document title says so ("Cancellation E2696NTCSANEG", "Notice of
/// cancellation", "Cancellation notice.pdf", "this bid has been withdrawn"). Titles are matched loosely; the long
/// description only on sentence-level wording so "cancellation fees" in a scope of work does not cancel a tender.
/// </summary>
public static partial class CancellationDetector
{
    /// <summary>The reason to record, or null when nothing in the snapshot says the tender was cancelled.</summary>
    public static string? Detect(TenderSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (TitlePattern().IsMatch(snapshot.Title))
        {
            return $"Title says cancelled: {Trim(snapshot.Title)}";
        }

        foreach (var document in snapshot.Documents)
        {
            if (TitlePattern().IsMatch(document.Title))
            {
                return $"Cancellation document published: {Trim(document.Title)}";
            }
        }

        var sentence = DescriptionPattern().Match(snapshot.Description ?? string.Empty);
        return sentence.Success ? $"Description says cancelled: {Trim(sentence.Value)}" : null;
    }

    public static bool IsCancellationTitle(string? title) => title is not null && TitlePattern().IsMatch(title);

    private static string Trim(string text) => text.Length <= 120 ? text.Trim() : text[..120].Trim() + "…";

    [GeneratedRegex(@"\b(cancel(l?ed|l?ation|s)?|withdraw(n|al)|retract(ed|ion))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TitlePattern();

    [GeneratedRegex(
        @"(notice\s+of\s+cancellation|cancellation\s+(notice|of\s+(this\s+)?(tender|bid|rfq|rfp|quotation|request))|(tender|bid|rfq|rfp|quotation|request\s+for\s+\w+)\s+(number\s+\S+\s+)?(is|was|has\s+been|have\s+been)\s+(hereby\s+)?(cancelled|canceled|withdrawn|retracted)|no\s+longer\s+(required|proceeding)|will\s+not\s+be\s+proceeding)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DescriptionPattern();
}
