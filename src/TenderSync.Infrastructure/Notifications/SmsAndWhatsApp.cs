using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Notifications;
using TenderSync.Domain.Enums;

namespace TenderSync.Infrastructure.Notifications;

/// <summary>SMS channel over <see cref="ISmsSender"/>: subject and body collapsed to one short text.</summary>
public sealed class SmsChannel(ISmsSender sender) : INotificationChannel
{
    public const int MaxLength = 320;

    public NotificationChannel Channel => NotificationChannel.Sms;

    public Task<NotificationSendResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return sender.IsConfigured
            ? sender.SendAsync(message.Recipient, Shorten(message), cancellationToken)
            : Task.FromResult(NotificationSendResult.Skip("SMS is not configured (Notifications:Sms:Provider is NoOp)."));
    }

    public static string Shorten(NotificationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var text = message.Body.StartsWith(message.Subject, StringComparison.Ordinal) ? message.Body : $"{message.Subject}\n{message.Body}";
        return text.Length <= MaxLength ? text : text[..(MaxLength - 1)] + "…";
    }
}

/// <summary>WhatsApp channel over <see cref="IWhatsAppSender"/>; a no-op in v1.</summary>
public sealed class WhatsAppChannel(IWhatsAppSender sender) : INotificationChannel
{
    public NotificationChannel Channel => NotificationChannel.WhatsApp;

    public Task<NotificationSendResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return sender.IsConfigured
            ? sender.SendAsync(message.Recipient, SmsChannel.Shorten(message), cancellationToken)
            : Task.FromResult(NotificationSendResult.Skip("WhatsApp is not available in v1 (Notifications:WhatsApp:Provider is NoOp)."));
    }
}

/// <summary>Default SMS sender: nothing is sent, the row is marked Skipped.</summary>
public sealed class NoOpSmsSender : ISmsSender
{
    public bool IsConfigured => false;

    public Task<NotificationSendResult> SendAsync(string phoneNumber, string text, CancellationToken cancellationToken = default) =>
        Task.FromResult(NotificationSendResult.Skip("SMS sender is NoOp."));
}

/// <summary>Default WhatsApp sender: nothing is sent (the Meta Cloud API integration is a later phase).</summary>
public sealed class NoOpWhatsAppSender : IWhatsAppSender
{
    public bool IsConfigured => false;

    public Task<NotificationSendResult> SendAsync(string phoneNumber, string text, CancellationToken cancellationToken = default) =>
        Task.FromResult(NotificationSendResult.Skip("WhatsApp sender is NoOp."));
}

/// <summary>
/// Clickatell Platform "messages" API stub (PLAN.md §7.6): POST /messages with an API key. Wired and shaped after the
/// public documentation but not exercised against a live account; keep Provider=NoOp until it has been.
/// </summary>
public sealed class ClickatellSmsSender(IHttpClientFactory httpClientFactory, IOptions<NotificationChannelOptions> options, ILogger<ClickatellSmsSender> logger) : ISmsSender
{
    public const string ClientName = "clickatell";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.Sms.ApiKey);

    public async Task<NotificationSendResult> SendAsync(string phoneNumber, string text, CancellationToken cancellationToken = default)
    {
        var sms = options.Value.Sms;
        if (!IsConfigured)
        {
            return NotificationSendResult.Skip("Clickatell API key is not configured.");
        }

        var client = httpClientFactory.CreateClient(ClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{sms.BaseUrl.TrimEnd('/')}/messages")
        {
            Content = JsonContent.Create(BuildPayload(phoneNumber, text, sms.From)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(sms.ApiKey);
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Clickatell returned {Status}: {Body}", (int)response.StatusCode, body);
                return NotificationSendResult.Failed($"Clickatell {(int)response.StatusCode}: {(body.Length <= 200 ? body : body[..200])}");
            }

            return NotificationSendResult.Ok();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Clickatell send failed");
            return NotificationSendResult.Failed($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public static object BuildPayload(string phoneNumber, string text, string? from) => new
    {
        messages = new[] { new { channel = "sms", to = phoneNumber.TrimStart('+'), content = text, from = string.IsNullOrWhiteSpace(from) ? null : from } },
    };
}
