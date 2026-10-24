using System.Net;
using System.Text.RegularExpressions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using TenderSync.Application.Abstractions.Notifications;
using TenderSync.Domain.Enums;

namespace TenderSync.Infrastructure.Notifications;

/// <summary>SMTP through MailKit (PLAN.md §7.6). The body is HTML; a text alternative is derived from it. Unconfigured → Skipped.</summary>
public sealed partial class EmailChannel(IOptions<NotificationChannelOptions> options, ILogger<EmailChannel> logger) : INotificationChannel
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public async Task<NotificationSendResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var email = options.Value.Email;
        if (!email.IsConfigured)
        {
            return NotificationSendResult.Skip("Email is not configured (Notifications:Email:SmtpHost and FromAddress).");
        }

        using var mime = Build(message, email);
        using var client = new SmtpClient { Timeout = email.TimeoutSeconds * 1000 };
        try
        {
            await client.ConnectAsync(email.SmtpHost, email.SmtpPort, Security(email.Security), cancellationToken);
            if (!string.IsNullOrWhiteSpace(email.SmtpUser))
            {
                await client.AuthenticateAsync(email.SmtpUser, email.SmtpPassword, cancellationToken);
            }

            var response = await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            return NotificationSendResult.Ok(mime.MessageId ?? response);
        }
        catch (Exception ex) when (ex is SmtpCommandException or SmtpProtocolException or AuthenticationException or IOException or System.Net.Sockets.SocketException or TimeoutException)
        {
            logger.LogWarning(ex, "SMTP send to {Host} failed", email.SmtpHost);
            return NotificationSendResult.Failed($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>The MIME message as it would be sent; public so tests can check it without an SMTP server.</summary>
    public static MimeMessage Build(NotificationMessage message, NotificationChannelOptions.EmailOptions email)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(email);
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(email.FromName, email.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.Recipient));
        mime.Subject = message.Subject;
        var html = message.Body;
        if (message.ActionUrl is not null && !html.Contains(message.ActionUrl, StringComparison.Ordinal))
        {
            html += $"<p><a href=\"{WebUtility.HtmlEncode(message.ActionUrl)}\">{WebUtility.HtmlEncode(message.ActionLabel ?? "Open")}</a></p>";
        }

        var builder = new BodyBuilder { HtmlBody = html, TextBody = ToText(html) };
        mime.Body = builder.ToMessageBody();
        return mime;
    }

    /// <summary>A readable plain-text alternative: block tags become line breaks, links keep their address, entities are decoded.</summary>
    public static string ToText(string html)
    {
        var text = BlockTags().Replace(html ?? string.Empty, "\n");
        text = Anchors().Replace(text, m => $"{m.Groups["text"].Value} ({m.Groups["href"].Value})");
        text = Tags().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        return BlankLines().Replace(text, "\n\n").Trim();
    }

    private static SecureSocketOptions Security(string value) => value.ToUpperInvariant() switch
    {
        "SSLONCONNECT" => SecureSocketOptions.SslOnConnect,
        "NONE" => SecureSocketOptions.None,
        "AUTO" => SecureSocketOptions.Auto,
        _ => SecureSocketOptions.StartTls,
    };

    [GeneratedRegex(@"<\s*(br|/p|/div|/h\d|/li|/tr|/table|/ul|/pre)\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockTags();

    [GeneratedRegex(@"<a\s[^>]*href=""(?<href>[^""]+)""[^>]*>(?<text>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex Anchors();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t]*\n\s*\n[ \t\n]*", RegexOptions.CultureInvariant)]
    private static partial Regex BlankLines();
}
