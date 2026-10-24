using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TenderSync.Domain.Common;

namespace TenderSync.Domain.Notifications;

/// <summary>
/// A daily window in which a preference does not want to be disturbed (PLAN.md §7.6), stored as JSON on the preference:
/// {"start":"22:00","end":"06:00","timeZone":"Africa/Johannesburg"}. A window may cross midnight. Messages that arrive in
/// the window are held until it ends.
/// </summary>
public sealed record QuietHours(
    [property: JsonPropertyName("start")] string Start,
    [property: JsonPropertyName("end")] string End,
    [property: JsonPropertyName("timeZone")] string? TimeZone)
{
    public const string DefaultTimeZone = "Africa/Johannesburg";

    public static QuietHours? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var value = DomainJson.Deserialize<QuietHours>(json);
        if (value is null || !TryParseTime(value.Start, out _) || !TryParseTime(value.End, out _))
        {
            throw new DomainException("Quiet hours need start and end times as HH:mm.");
        }

        return value;
    }

    public string ToJson() => DomainJson.Serialize(this);

    /// <summary>True when <paramref name="at"/> falls inside the window in the window's time zone.</summary>
    public bool Contains(DateTimeOffset at)
    {
        var local = TimeOnly.FromDateTime(ToLocal(at).DateTime);
        if (!TryParseTime(Start, out var start) || !TryParseTime(End, out var end) || start == end)
        {
            return false;
        }

        return start < end ? local >= start && local < end : local >= start || local < end;
    }

    /// <summary>The next moment the window ends, or null when <paramref name="at"/> is outside it.</summary>
    public DateTimeOffset? EndsAt(DateTimeOffset at)
    {
        if (!Contains(at) || !TryParseTime(End, out var end))
        {
            return null;
        }

        var local = ToLocal(at);
        var endToday = new DateTimeOffset(local.Date.Add(end.ToTimeSpan()), local.Offset);
        var result = endToday > local ? endToday : endToday.AddDays(1);
        return result.ToUniversalTime();
    }

    private DateTimeOffset ToLocal(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, Zone(TimeZone));

    private static bool TryParseTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static TimeZoneInfo Zone(string? id)
    {
        foreach (var candidate in new[] { id, DefaultTimeZone, "South Africa Standard Time" })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate);
            }
            catch (TimeZoneNotFoundException)
            {
                // next candidate
            }
            catch (InvalidTimeZoneException)
            {
                // next candidate
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("SAST", TimeSpan.FromHours(2), "South Africa Standard Time", "SAST");
    }

    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Start) && string.IsNullOrWhiteSpace(End);
}
