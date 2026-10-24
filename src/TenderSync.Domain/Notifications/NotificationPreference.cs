using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Notifications;

/// <summary>Which notification types a user wants on a channel, and where to send them.</summary>
public sealed class NotificationPreference : Entity
{
    private NotificationPreference()
    {
    }

    public Guid UserId { get; private set; }

    public NotificationChannel Channel { get; private set; }

    public bool Enabled { get; private set; }

    /// <summary>Email address, Telegram chat id, Teams webhook URL or phone number depending on the channel.</summary>
    public string Address { get; private set; } = null!;

    public NotificationType[] Types { get; private set; } = [];

    /// <summary>Optional quiet hours as JSON, e.g. {"start":"22:00","end":"06:00","timeZone":"Africa/Johannesburg"}.</summary>
    public string? QuietHoursJson { get; private set; }

    public static NotificationPreference Create(Guid userId, NotificationChannel channel, string address, IEnumerable<NotificationType> types, bool enabled = true)
    {
        var preference = new NotificationPreference { UserId = userId, Channel = channel };
        preference.Update(address, types, enabled, quietHoursJson: null);
        return preference;
    }

    public void Update(string address, IEnumerable<NotificationType> types, bool enabled, string? quietHoursJson)
    {
        Address = Guard.NotEmpty(address, nameof(address));
        Types = [.. types.Distinct()];
        Enabled = enabled;
        QuietHoursJson = Notifications.QuietHours.Parse(quietHoursJson)?.ToJson();
    }

    public bool Wants(NotificationType type) => Enabled && Types.Contains(type);

    public QuietHours? QuietHours => Notifications.QuietHours.Parse(QuietHoursJson);
}
