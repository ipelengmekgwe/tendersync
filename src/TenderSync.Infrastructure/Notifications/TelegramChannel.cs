using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Notifications;
using TenderSync.Domain.Enums;

namespace TenderSync.Infrastructure.Notifications;

/// <summary>Telegram Bot API sendMessage (PLAN.md §7.6): HTML parse mode, an inline button to the tender page, 4 096-character limit.</summary>
public sealed class TelegramChannel(IHttpClientFactory httpClientFactory, IOptions<NotificationChannelOptions> options, ILogger<TelegramChannel> logger) : INotificationChannel
{
    public const string ClientName = "telegram";
    public const int MaxTextLength = 4096;

    public NotificationChannel Channel => NotificationChannel.Telegram;

    public async Task<NotificationSendResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var telegram = options.Value.Telegram;
        if (!telegram.IsConfigured)
        {
            return NotificationSendResult.Skip("Telegram is not configured (Notifications:Telegram:BotToken).");
        }

        var client = httpClientFactory.CreateClient(ClientName);
        var url = $"{telegram.BaseUrl.TrimEnd('/')}/bot{telegram.BotToken}/sendMessage";
        try
        {
            using var response = await client.PostAsJsonAsync(url, BuildPayload(message), cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Telegram sendMessage returned {Status}: {Body}", (int)response.StatusCode, body);
                return NotificationSendResult.Failed($"Telegram {(int)response.StatusCode}: {Describe(body)}");
            }

            using var json = JsonDocument.Parse(body);
            var ok = json.RootElement.TryGetProperty("ok", out var okElement) && okElement.GetBoolean();
            if (!ok)
            {
                return NotificationSendResult.Failed($"Telegram: {Describe(body)}");
            }

            var id = json.RootElement.TryGetProperty("result", out var result) && result.TryGetProperty("message_id", out var messageId) ? messageId.ToString() : null;
            return NotificationSendResult.Ok(id);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Telegram send failed");
            return NotificationSendResult.Failed($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>The request body; public so tests can check the shape without a bot token.</summary>
    public static object BuildPayload(NotificationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var text = message.Body.Length <= MaxTextLength ? message.Body : message.Body[..(MaxTextLength - 1)] + "…";
        object? replyMarkup = message.ActionUrl is null ? null : new { inline_keyboard = new[] { new[] { new { text = message.ActionLabel ?? "Open", url = message.ActionUrl } } } };
        return new
        {
            chat_id = message.Recipient,
            text,
            parse_mode = "HTML",
            disable_web_page_preview = true,
            reply_markup = replyMarkup,
        };
    }

    private static string Describe(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.TryGetProperty("description", out var description) ? description.GetString() ?? body : body;
        }
        catch (JsonException)
        {
            return body.Length <= 200 ? body : body[..200];
        }
    }
}
