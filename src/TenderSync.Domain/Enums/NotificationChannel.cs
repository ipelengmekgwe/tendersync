namespace TenderSync.Domain.Enums;

/// <summary>Delivery channel. Sms and WhatsApp are abstractions with no-op implementations in v1.</summary>
public enum NotificationChannel
{
    Email = 0,
    Telegram = 1,
    Teams = 2,
    Sms = 3,
    WhatsApp = 4,
}
