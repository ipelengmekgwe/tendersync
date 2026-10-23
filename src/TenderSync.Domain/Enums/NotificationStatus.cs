namespace TenderSync.Domain.Enums;

/// <summary>Delivery state of a notification row.</summary>
public enum NotificationStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
    Skipped = 3,
}
