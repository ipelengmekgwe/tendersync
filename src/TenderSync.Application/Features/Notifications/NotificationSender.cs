using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Notifications;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Notifications;

namespace TenderSync.Application.Features.Notifications;

public sealed record SendOutcome(Guid NotificationId, NotificationStatus Status, bool Attempted, string? Error);

/// <summary>
/// Delivers one stored notification through its channel and records the result: Sent, Pending with a backoff (retry),
/// Failed after five attempts, or Skipped when the channel is not configured (PLAN.md §7.6). The sweeper calls
/// <see cref="SendDueAsync"/> every minute for rows held by quiet hours or a retry and for rows created by the API.
/// </summary>
public sealed class NotificationSender(
    IAppDbContext db,
    IEnumerable<INotificationChannel> channels,
    IOptions<NotificationOptions> options,
    IDateTimeProvider clock,
    ILogger<NotificationSender> logger)
{
    public async Task<SendOutcome> SendAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await db.Notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            logger.LogWarning("Notification {NotificationId} no longer exists", notificationId);
            return new SendOutcome(notificationId, NotificationStatus.Skipped, false, "not found");
        }

        var outcome = await SendAsync(notification, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return outcome;
    }

    public async Task<int> SendDueAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var settings = options.Value;
        var due = await db.Notifications.GetDueAsync(now, now.AddSeconds(-settings.SweepOlderThanSeconds), settings.SweepBatchSize, cancellationToken);
        var attempted = 0;
        foreach (var notification in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await SendAsync(notification, cancellationToken);
            attempted += outcome.Attempted ? 1 : 0;
            await db.SaveChangesAsync(cancellationToken);
        }

        return attempted;
    }

    private async Task<SendOutcome> SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (notification.Status != NotificationStatus.Pending)
        {
            return new SendOutcome(notification.Id, notification.Status, false, null);
        }

        if (notification.NotBefore is { } notBefore && notBefore > now)
        {
            return new SendOutcome(notification.Id, notification.Status, false, "not due");
        }

        var channel = channels.FirstOrDefault(c => c.Channel == notification.Channel);
        if (channel is null)
        {
            notification.Skip($"No {notification.Channel} channel is registered.");
            return new SendOutcome(notification.Id, notification.Status, false, notification.Error);
        }

        var (actionUrl, actionLabel) = ReadAction(notification.PayloadJson);
        NotificationSendResult result;
        try
        {
            result = await channel.SendAsync(new NotificationMessage(notification.Recipient, notification.Subject, notification.Body, actionUrl, actionLabel), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = NotificationSendResult.Failed($"{ex.GetType().Name}: {ex.Message}");
        }

        if (result.Skipped)
        {
            notification.Skip(result.Error ?? "Channel not configured.");
            logger.LogInformation("Notification {NotificationId} ({Type} via {Channel}) skipped: {Reason}", notification.Id, notification.Type, notification.Channel, notification.Error);
        }
        else if (result.Success)
        {
            notification.MarkSent(now);
            logger.LogInformation("Notification {NotificationId} ({Type} via {Channel}) sent to {Recipient}", notification.Id, notification.Type, notification.Channel, Mask(notification.Recipient));
        }
        else
        {
            var willRetry = notification.MarkFailed(result.Error ?? "Unknown error", now);
            logger.LogWarning("Notification {NotificationId} ({Type} via {Channel}) failed on attempt {Attempt}: {Error}; retry {WillRetry}", notification.Id, notification.Type, notification.Channel, notification.Attempts, result.Error, willRetry);
        }

        return new SendOutcome(notification.Id, notification.Status, true, notification.Error);
    }

    private static (string? Url, string? Label) ReadAction(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            var url = root.TryGetProperty("actionUrl", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
            var label = root.TryGetProperty("actionLabel", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null;
            return (url, label);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    /// <summary>Recipients are personal data: logs show only enough to recognise them.</summary>
    public static string Mask(string recipient)
    {
        if (string.IsNullOrEmpty(recipient))
        {
            return string.Empty;
        }

        var at = recipient.IndexOf('@', StringComparison.Ordinal);
        if (at > 1)
        {
            return recipient[..2] + "***" + recipient[at..];
        }

        return recipient.Length <= 4 ? "***" : "***" + recipient[^4..];
    }
}
