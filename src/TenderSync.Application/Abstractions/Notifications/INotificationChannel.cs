using TenderSync.Domain.Enums;

namespace TenderSync.Application.Abstractions.Notifications;

/// <summary>One delivery channel (email, Telegram, Teams, SMS, WhatsApp). Implemented in WP10.</summary>
public interface INotificationChannel
{
    NotificationChannel Channel { get; }

    Task<NotificationSendResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}

public sealed record NotificationMessage(string Recipient, string Subject, string Body, string? ActionUrl, string? ActionLabel);

public sealed record NotificationSendResult(bool Success, string? ProviderMessageId, string? Error)
{
    public static NotificationSendResult Ok(string? providerMessageId = null) => new(true, providerMessageId, null);

    public static NotificationSendResult Failed(string error) => new(false, null, error);
}
