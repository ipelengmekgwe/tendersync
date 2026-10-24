using TenderSync.Domain.Enums;

namespace TenderSync.Application.Abstractions.Notifications;

/// <summary>One delivery channel (email, Telegram, Teams, SMS, WhatsApp). Infrastructure registers one per <see cref="NotificationChannel"/>.</summary>
public interface INotificationChannel
{
    NotificationChannel Channel { get; }

    Task<NotificationSendResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// A rendered message. <paramref name="Body"/> is HTML for email and Telegram, markdown-ish plain text for Teams, and plain
/// text for SMS and WhatsApp; the templates decide, the channels only transport.
/// </summary>
public sealed record NotificationMessage(string Recipient, string Subject, string Body, string? ActionUrl, string? ActionLabel);

/// <summary>Outcome of one send. Skipped means the channel is not configured or is a no-op; the row is marked Skipped, not retried.</summary>
public sealed record NotificationSendResult(bool Success, string? ProviderMessageId, string? Error, bool Skipped = false)
{
    public static NotificationSendResult Ok(string? providerMessageId = null) => new(true, providerMessageId, null);

    public static NotificationSendResult Failed(string error) => new(false, null, error);

    public static NotificationSendResult Skip(string reason) => new(false, null, reason, Skipped: true);
}

/// <summary>SMS transport (PLAN.md §7.6): a no-op by default, Clickatell as the first real provider.</summary>
public interface ISmsSender
{
    bool IsConfigured { get; }

    Task<NotificationSendResult> SendAsync(string phoneNumber, string text, CancellationToken cancellationToken = default);
}

/// <summary>WhatsApp transport (PLAN.md §7.6): a no-op in v1; the Meta Cloud API comes later.</summary>
public interface IWhatsAppSender
{
    bool IsConfigured { get; }

    Task<NotificationSendResult> SendAsync(string phoneNumber, string text, CancellationToken cancellationToken = default);
}
