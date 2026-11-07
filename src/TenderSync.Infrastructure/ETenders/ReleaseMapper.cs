using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Infrastructure.ETenders;

/// <summary>
/// OCDS release → <see cref="TenderSnapshot"/>. Encodes the feed's quirks (PLAN.md §2): tender.title is the bid number,
/// tender.description holds the human title, dates may be empty strings, amounts may be strings, status is always empty.
/// </summary>
public sealed class ReleaseMapper(IOptions<ETendersOptions> options)
{
    public const int MaxTitleLength = 200;

    private readonly ETendersOptions _options = options.Value;

    public TenderSnapshot ToSnapshot(OcdsReleaseEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var release = envelope.Release;
        var tender = release.Tender ?? throw new InvalidDataException($"Release {release.Ocid} has no tender section.");
        var ocid = Require(release.Ocid, "ocid");
        var tenderId = Require(tender.Id, "tender.id");
        var buyer = release.Buyer ?? tender.ProcuringEntity ?? throw new InvalidDataException($"Release {ocid} has no buyer.");
        var description = (tender.Description ?? string.Empty).Trim();
        var bidNumber = string.IsNullOrWhiteSpace(tender.Title) ? tenderId : tender.Title.Trim();

        return new TenderSnapshot(
            Ocid: ocid,
            ExternalTenderId: tenderId,
            BidNumber: bidNumber,
            Title: TitleFrom(description, bidNumber),
            Description: description,
            BuyerExternalId: Require(buyer.Id, "buyer.id"),
            BuyerName: string.IsNullOrWhiteSpace(buyer.Name) ? $"Buyer {buyer.Id}" : buyer.Name.Trim(),
            MainCategory: ParseCategory(tender.MainProcurementCategory),
            AdditionalCategories: tender.AdditionalProcurementCategories.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim().ToLowerInvariant()).ToList(),
            ProcurementMethod: Clean(tender.ProcurementMethod),
            ProcurementMethodDetails: Clean(tender.ProcurementMethodDetails),
            ValueAmount: ParseAmount(tender.Value?.Amount),
            ValueCurrency: Clean(tender.Value?.Currency),
            PublishedAt: ParseDate(tender.TenderPeriod?.StartDate) ?? ParseDate(release.Date) ?? DateTimeOffset.UnixEpoch,
            ClosingAt: ParseDate(tender.TenderPeriod?.EndDate),
            SourceUrl: string.Format(CultureInfo.InvariantCulture, _options.TenderDetailsUrlFormat, Uri.EscapeDataString(tenderId)),
            ReleaseId: string.IsNullOrWhiteSpace(release.Id) ? $"{ocid}-{release.Date}" : release.Id,
            ReleaseDate: ParseDate(release.Date) ?? DateTimeOffset.UnixEpoch,
            ReleaseTag: release.Tag.Count == 0 ? "unknown" : string.Join(",", release.Tag),
            RawJson: envelope.RawJson,
            Documents: tender.Documents
                .Where(d => !string.IsNullOrWhiteSpace(d.Id) && !string.IsNullOrWhiteSpace(d.Url))
                .Select(d => new TenderDocumentSnapshot(
                    d.Id!.Trim(),
                    string.IsNullOrWhiteSpace(d.Title) ? (Clean(d.Description) ?? d.Id!) : d.Title.Trim(),
                    d.Url!.Trim(),
                    Clean(d.Format),
                    Clean(d.Language),
                    ParseDate(d.DatePublished),
                    ParseDate(d.DateModified)))
                .ToList(),
            HasAwards: release.Awards.Count > 0);
    }

    /// <summary>First line or sentence of the description, trimmed to <see cref="MaxTitleLength"/>; the bid number when there is none.</summary>
    public static string TitleFrom(string description, string fallback)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return fallback;
        }

        var firstLine = description.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? description;
        var sentenceEnd = firstLine.IndexOf(". ", StringComparison.Ordinal);
        var title = sentenceEnd > 40 ? firstLine[..sentenceEnd] : firstLine;
        title = title.Trim().TrimEnd('.', ' ');

        if (title.Length > MaxTitleLength)
        {
            var cut = title.LastIndexOf(' ', MaxTitleLength - 1);
            title = (cut > MaxTitleLength / 2 ? title[..cut] : title[..(MaxTitleLength - 1)]) + "…";
        }

        return title.Length == 0 ? fallback : title;
    }

    public static ProcurementCategory ParseCategory(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "goods" => ProcurementCategory.Goods,
        "services" => ProcurementCategory.Services,
        "works" => ProcurementCategory.Works,
        _ => ProcurementCategory.Unknown,
    };

    public static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;
    }

    public static decimal? ParseAmount(JsonElement? element)
    {
        if (element is not { } value)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) => number,
            _ => null,
        };
    }

    private static string Require(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException($"Release is missing {field}.") : value.Trim();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
