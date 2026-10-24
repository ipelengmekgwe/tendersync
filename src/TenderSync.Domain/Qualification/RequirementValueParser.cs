using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TenderSync.Domain.Tenders;

namespace TenderSync.Domain.Qualification;

/// <summary>
/// Reads thresholds out of the free text the extractor stored ("level 4", "R10 million", "3 references of R2m in the last
/// 5 years", "grade 7GB"). Every parser returns null when the text does not state the value, so evaluators answer Unknown
/// rather than guessing.
/// </summary>
public static partial class RequirementValueParser
{
    private static readonly Dictionary<string, int> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10,
    };

    /// <summary>The "value" member of the requirement's value JSON, as written by extraction, or null.</summary>
    public static string? ValueText(TenderRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        if (string.IsNullOrWhiteSpace(requirement.ValueJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(requirement.ValueJson);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The value text when present, else the requirement text: thresholds are sometimes only in the sentence.</summary>
    public static string TextToParse(TenderRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        var value = ValueText(requirement);
        return string.IsNullOrWhiteSpace(value) ? requirement.Text : $"{value} {requirement.Text}";
    }

    /// <summary>The worst acceptable B-BBEE level: "level 4", "Level 1 to 4", "level 2 or better" give 4, 4, 2.</summary>
    public static int? BbbeeLevel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var levels = LevelPattern().Matches(text).SelectMany(m => m.Groups["n"].Captures.Select(c => int.Parse(c.Value, CultureInfo.InvariantCulture))).Where(n => n is >= 1 and <= 8).ToList();
        if (levels.Count > 0)
        {
            return levels.Max();
        }

        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bare) && bare is >= 1 and <= 8 ? bare : null;
    }

    /// <summary>A Rand amount: "R10 million", "R10m", "R5,000,000", "R 2.5 mil", "ZAR 3 000 000", "R2,5 million".</summary>
    public static decimal? Rand(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = RandPattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var number = match.Groups["num"].Value.Replace(" ", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (DecimalCommaPattern().IsMatch(number))
        {
            number = number.Replace(',', '.');
        }
        else
        {
            number = number.Replace(",", string.Empty, StringComparison.Ordinal);
        }

        if (!decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            return null;
        }

        var unit = match.Groups["unit"].Value.ToLowerInvariant();
        var factor = unit switch
        {
            "k" or "thousand" => 1_000m,
            "m" or "mil" or "million" => 1_000_000m,
            "bn" or "billion" => 1_000_000_000m,
            _ => 1m,
        };
        return amount * factor;
    }

    /// <summary>"5 years", "five (5) years", "minimum of 3 years' experience" give 5, 5, 3.</summary>
    public static int? Years(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = YearsPattern().Match(text);
        return match.Success ? int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>The first count in the text, as digits or a number word: "3 references", "three (3) contactable references" give 3.</summary>
    public static int? Count(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var digits = CountPattern().Match(text);
        if (digits.Success)
        {
            return int.Parse(digits.Groups["n"].Value, CultureInfo.InvariantCulture);
        }

        foreach (var token in WordPattern().Matches(text).Select(m => m.Value))
        {
            if (NumberWords.TryGetValue(token, out var n))
            {
                return n;
            }
        }

        return null;
    }

    /// <summary>CIDB grading "7GB", "grade 7 GB or higher", "Grading 5 CE": the grade and the class designation.</summary>
    public static (int Grade, string? Class)? CidbGrade(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = CidbPattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var grade = int.Parse(match.Groups["g"].Value, CultureInfo.InvariantCulture);
        var cls = match.Groups["c"].Success ? match.Groups["c"].Value.ToUpperInvariant() : null;
        return grade is >= 1 and <= 9 ? (grade, cls) : null;
    }

    /// <summary>Reference-project rule: how many, each worth at least how much, within how many years. Any part may be null.</summary>
    public static ReferenceSpec References(string? text) => new(Count(text), Rand(text), WindowYears(text));

    private static int? WindowYears(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = WindowPattern().Match(text);
        return match.Success ? int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture) : null;
    }

    [GeneratedRegex(@"level\s*(?<n>\d)(?:\s*(?:-|–|to|or)\s*(?<n>\d))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LevelPattern();

    [GeneratedRegex(@"(?:R|ZAR)\s*(?<num>\d(?:[\d\s ,\.]*\d)?)\s*(?<unit>million|mil|m|k|thousand|bn|billion)?(?![A-Za-z\d])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RandPattern();

    [GeneratedRegex(@"^\d+,\d{1,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalCommaPattern();

    [GeneratedRegex(@"(?<n>\d{1,3})\s*\)?\s*(?:\(\d+\)\s*)?\+?\s*(?:consecutive\s+|financial\s+)?years?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex YearsPattern();

    [GeneratedRegex(@"(?<![\d,\.R])(?<n>\d{1,3})(?![\d,\.]|\s*(?:years?|months?|%|/))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CountPattern();

    [GeneratedRegex(@"[A-Za-z]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"(?:grad(?:e|ing)\s*)?(?<g>\d)\s*(?<c>GB|CE|ME|EP|EB|SW|SB|SC|SD|SE|SF|SG|SH|SI|SJ|SK|SL|SM|SN|SO|SP|SQ|SR)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CidbPattern();

    [GeneratedRegex(@"(?:last|past|preceding|previous|within)\s*(?:the\s*)?(?<n>\d{1,2})\s*(?:\(\d+\)\s*)?years?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WindowPattern();
}

/// <summary>Parsed reference-project rule; nulls mean the document did not state that part.</summary>
public sealed record ReferenceSpec(int? Count, decimal? MinValue, int? WithinYears);
