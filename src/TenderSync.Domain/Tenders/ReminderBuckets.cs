using System.Globalization;
using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Tenders;

/// <summary>
/// Reminder buckets (PLAN.md §7.6, §7.7) counted in South African calendar days, so a tender closing at 11:00 tomorrow is
/// "1 day" whether the job runs at 06:00 or at 23:30 tonight. SAST has no daylight saving, so a fixed +02:00 is exact.
/// </summary>
public static class ReminderBuckets
{
    public static readonly TimeSpan SastOffset = TimeSpan.FromHours(2);

    /// <summary>Whole South African calendar days from now's date to the target's date; negative when the target is past.</summary>
    public static int DaysUntil(DateTimeOffset target, DateTimeOffset now) =>
        DateOnly.FromDateTime(target.ToOffset(SastOffset).DateTime).DayNumber - DateOnly.FromDateTime(now.ToOffset(SastOffset).DateTime).DayNumber;

    /// <summary>ClosingSoon1d for a tender closing today or tomorrow, 3d within three days, 7d within seven; null outside the window or once closed.</summary>
    public static NotificationType? Closing(DateTimeOffset closingAt, DateTimeOffset now)
    {
        if (closingAt <= now)
        {
            return null;
        }

        var days = DaysUntil(closingAt, now);
        return days switch
        {
            <= 1 => NotificationType.ClosingSoon1d,
            <= 3 => NotificationType.ClosingSoon3d,
            <= 7 => NotificationType.ClosingSoon7d,
            _ => null,
        };
    }

    /// <summary>True when the briefing is within the next <paramref name="days"/> South African days and has not started.</summary>
    public static bool BriefingDue(DateTimeOffset briefingAt, DateTimeOffset now, int days = 2) =>
        briefingAt > now && DaysUntil(briefingAt, now) <= days;

    /// <summary>The dedupe bucket for a dated reminder: the target's South African date, so a moved date reminds again.</summary>
    public static string DateBucket(DateTimeOffset target) => target.ToOffset(SastOffset).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
}
