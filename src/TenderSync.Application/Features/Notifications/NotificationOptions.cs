using System.ComponentModel.DataAnnotations;

namespace TenderSync.Application.Features.Notifications;

/// <summary>Notification behaviour (PLAN.md §7.6, §9), bound from the "Notifications" section. Channel credentials live in Infrastructure options under the same section.</summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Base of the dashboard links in messages (/tenders/{id}).</summary>
    [Required]
    public string DashboardBaseUrl { get; set; } = "http://localhost:3000";

    /// <summary>A NewMatch for a tender closing within this many days bypasses quiet hours.</summary>
    [Range(0, 30)]
    public int UrgentClosingDays { get; set; } = 2;

    /// <summary>The sweeper leaves rows younger than this to the per-message job that was enqueued with them.</summary>
    [Range(0, 3600)]
    public int SweepOlderThanSeconds { get; set; } = 30;

    [Range(1, 500)]
    public int SweepBatchSize { get; set; } = 50;

    /// <summary>Tenders closing within this many days appear in the daily digest.</summary>
    [Range(1, 60)]
    public int DigestClosingDays { get; set; } = 7;

    /// <summary>Top missing items shown per message (PLAN.md §9).</summary>
    [Range(1, 10)]
    public int MaxMissingItems { get; set; } = 3;

    public string TenderUrl(Guid tenderId) => $"{DashboardBaseUrl.TrimEnd('/')}/tenders/{tenderId}";
}
