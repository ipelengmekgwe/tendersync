using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using TenderSync.Application.Abstractions.Notifications;
using TenderSync.Application.Features.Notifications;
using TenderSync.Domain.Enums;

namespace TenderSync.Infrastructure.Notifications;

/// <summary>Microsoft Teams incoming webhook with an Adaptive Card (PLAN.md §7.6). The recipient is the webhook URL from the preference.</summary>
public sealed class TeamsChannel(IHttpClientFactory httpClientFactory, ILogger<TeamsChannel> logger) : INotificationChannel
{
    public const string ClientName = "teams";

    public NotificationChannel Channel => NotificationChannel.Teams;

    public async Task<NotificationSendResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        // Checked again here, not only when the preference is saved: rows created before the allowlist existed still hold
        // whatever URL was typed in.
        if (!Uri.TryCreate(message.Recipient, UriKind.Absolute, out var webhook) || !TeamsWebhook.IsAllowed(webhook))
        {
            return NotificationSendResult.Failed("Teams recipient must be an https Teams or Power Automate webhook URL.");
        }

        var client = httpClientFactory.CreateClient(ClientName);
        try
        {
            using var response = await client.PostAsJsonAsync(webhook, BuildCard(message), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // The response body is neither logged nor stored: the user who owns the preference can read the error.
                logger.LogWarning("Teams webhook returned {Status}", (int)response.StatusCode);
                return NotificationSendResult.Failed($"Teams webhook answered HTTP {(int)response.StatusCode}.");
            }

            return NotificationSendResult.Ok();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Teams send failed");
            return NotificationSendResult.Failed($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>The webhook payload: a message with one Adaptive Card 1.4 attachment; public so tests can check the shape.</summary>
    public static object BuildCard(NotificationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var body = new List<object>
        {
            new { type = "TextBlock", text = message.Subject, weight = "Bolder", size = "Medium", wrap = true },
            new { type = "TextBlock", text = message.Body, wrap = true },
        };
        var actions = message.ActionUrl is null ? [] : new object[] { new { type = "Action.OpenUrl", title = message.ActionLabel ?? "Open", url = message.ActionUrl } };
        return new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    contentUrl = (string?)null,
                    content = new
                    {
                        type = "AdaptiveCard",
                        version = "1.4",
                        body,
                        actions,
                        msteams = new { width = "Full" },
                    },
                },
            },
        };
    }
}
