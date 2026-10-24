using System.ComponentModel.DataAnnotations;

namespace TenderSync.Application.Features.Status;

/// <summary>Status tracking and reminders (PLAN.md §7.7), bound from the "Status" section.</summary>
public sealed class StatusOptions
{
    public const string SectionName = "Status";

    /// <summary>Matched tenders closing after now − this many days are re-fetched from the source each morning.</summary>
    [Range(0, 365)]
    public int RefreshLookbackDays { get; set; } = 7;

    /// <summary>Cap per refresh run; the oldest-checked tenders go first, the rest wait for the next run.</summary>
    [Range(1, 10_000)]
    public int RefreshMaxTenders { get; set; } = 500;

    /// <summary>Pause between per-ocid fetches so the refresh stays polite to the portal.</summary>
    [Range(0, 10_000)]
    public int RefreshSpacingMs { get; set; } = 250;

    /// <summary>A closing date within this many days makes a tender ClosingSoon.</summary>
    [Range(1, 60)]
    public int ClosingSoonDays { get; set; } = 7;

    /// <summary>Briefing reminders go out this many South African days before the briefing.</summary>
    [Range(0, 30)]
    public int BriefingReminderDays { get; set; } = 2;

    /// <summary>Company documents expiring within this many days raise DocumentExpiring.</summary>
    [Range(1, 365)]
    public int DocumentExpiryDays { get; set; } = 30;

    [Range(1, 10_000)]
    public int ComputeBatchSize { get; set; } = 500;
}
