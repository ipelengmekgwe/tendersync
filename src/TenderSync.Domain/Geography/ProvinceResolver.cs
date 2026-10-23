using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Geography;

/// <summary>Result of resolving a buyer name.</summary>
public sealed record BuyerResolution(BuyerType Type, Province Province);

/// <summary>
/// Infers buyer type and province from a buyer name, since the eTenders feed carries neither (PLAN.md Appendix C).
/// Order: provincial prefix rules, municipality lookup, state-owned entity patterns, national department patterns.
/// </summary>
public static partial class ProvinceResolver
{
    private static readonly string[] SoeMarkers =
    [
        "soc", "(pty)", "pty ltd", "ltd", "limited", "agency", "authority", "corporation", "company", "fund", "bank",
        "board", "council", "seta", "institute", "commission", "regulator", "eskom", "transnet", "prasa", "sanral",
        "denel", "armscor", "sita", "sabc", "sassa", "nsfas", "rand water", "umgeni", "post office", "airports",
        "ports", "infraco", "sentech", "csir", "saqa", "weather service", "research council", "development bank",
        "land bank", "road agency", "rail", "forestry company", "nuclear", "petroleum", "acsa", "telkom", "safcol",
    ];

    private static readonly string[] NationalMarkers =
    [
        "department of", "national ", "dept ", "office of the", "parliament", "treasury", "presidency", "statistics",
        "public works", "home affairs", "defence", "police", "correctional", "justice", "health", "education", "labour",
        "social development", "trade", "mineral", "energy", "water and sanitation", "transport", "communications",
        "cooperative governance", "agriculture", "environment", "tourism", "sport", "arts", "science", "higher education",
        "basic education", "international relations", "public service", "public enterprises", "small business", "women",
    ];

    public static BuyerResolution Resolve(string? buyerName, Func<string, Province?>? municipalityLookup = null)
    {
        if (string.IsNullOrWhiteSpace(buyerName))
        {
            return new BuyerResolution(BuyerType.Unknown, Province.Unknown);
        }

        var name = buyerName.Trim();
        var lower = name.ToLowerInvariant();

        // 1. "Western Cape - Health", "Gauteng: Department of Education", "KZN Health".
        var prefixProvince = ProvincePrefix(name);
        if (prefixProvince != Province.Unknown)
        {
            return new BuyerResolution(IsMunicipal(lower) ? BuyerType.Municipal : BuyerType.Provincial, prefixProvince);
        }

        // 2. Municipalities via the seeded lookup table.
        if (IsMunicipal(lower))
        {
            var province = municipalityLookup?.Invoke(NormaliseName(name)) ?? Province.Unknown;
            return new BuyerResolution(BuyerType.Municipal, province);
        }

        // 3. State-owned entities and public bodies.
        if (SoeMarkers.Any(marker => lower.Contains(marker, StringComparison.Ordinal)))
        {
            return new BuyerResolution(BuyerType.StateOwnedEntity, Province.National);
        }

        // 4. National departments.
        if (NationalMarkers.Any(marker => lower.Contains(marker, StringComparison.Ordinal)))
        {
            return new BuyerResolution(BuyerType.National, Province.National);
        }

        return new BuyerResolution(BuyerType.Other, Province.Unknown);
    }

    /// <summary>Province from a leading "Province - " / "Province: " / "Province Department" pattern, else Unknown.</summary>
    public static Province ProvincePrefix(string name)
    {
        var match = PrefixPattern().Match(name);
        if (!match.Success)
        {
            return Province.Unknown;
        }

        return ProvinceInfo.Parse(match.Groups["province"].Value);
    }

    /// <summary>
    /// Lower-case, diacritics stripped, municipality suffixes and "city of" removed, non-alphanumerics collapsed to single spaces.
    /// "Greater Tzaneen Local Municipality" → "greater tzaneen"; "City of Cape Town Metropolitan Municipality" → "cape town".
    /// </summary>
    public static string NormaliseName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var stripped = StripDiacritics(name).ToLowerInvariant();
        stripped = SuffixPattern().Replace(stripped, " ");
        stripped = CityOfPattern().Replace(stripped, " ");
        stripped = NonAlphanumeric().Replace(stripped, " ");
        return WhitespaceRun().Replace(stripped, " ").Trim();
    }

    private static bool IsMunicipal(string lower) =>
        lower.Contains("municipal", StringComparison.Ordinal) || lower.Contains("metro", StringComparison.Ordinal) || lower.StartsWith("city of ", StringComparison.Ordinal);

    private static string StripDiacritics(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"^\s*(?<province>eastern cape|free state|gauteng|kwazulu[- ]?natal|kzn|limpopo|mpumalanga|north west|northern cape|western cape)\s*(?:-|:|–|—|province|provincial|department|dept|\s)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PrefixPattern();

    [GeneratedRegex(@"\b(metropolitan|district|local)?\s*municipality\b|\bmetro\b", RegexOptions.CultureInvariant)]
    private static partial Regex SuffixPattern();

    [GeneratedRegex(@"\bcity of\b", RegexOptions.CultureInvariant)]
    private static partial Regex CityOfPattern();

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRun();
}
