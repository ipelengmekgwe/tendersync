namespace TenderSync.Domain.Enums;

/// <summary>Reasons a notification is sent (PLAN.md §7.6).</summary>
public enum NotificationType
{
    NewMatch = 0,
    QualificationReady = 1,
    ClosingSoon7d = 2,
    ClosingSoon3d = 3,
    ClosingSoon1d = 4,
    BriefingReminder = 5,
    TenderChanged = 6,
    AwardPublished = 7,
    PipelineFailure = 8,
    DocumentExpiring = 9,
    DailyDigest = 10,
    Test = 11,
}
