namespace TenderSync.Application.Common;

/// <summary>SAST (UTC+2, no daylight saving). Used for schedules and display; storage stays UTC.</summary>
public static class SouthAfricaTime
{
    public static TimeZoneInfo Zone { get; } = Find();

    public static DateTimeOffset ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone);

    private static TimeZoneInfo Find()
    {
        foreach (var id in new[] { "Africa/Johannesburg", "South Africa Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // try the next id
            }
            catch (InvalidTimeZoneException)
            {
                // try the next id
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("SAST", TimeSpan.FromHours(2), "South Africa Standard Time", "SAST");
    }
}
