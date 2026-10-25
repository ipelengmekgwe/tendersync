using System.Text.RegularExpressions;

namespace TenderSync.Infrastructure.Notifications;

/// <summary>
/// Hides credentials that channel providers put in the URL path, so outgoing-call telemetry never records them: the
/// Telegram Bot API takes the bot token as a path segment (/bot{token}/sendMessage), and Teams and Power Automate
/// webhook URLs are bearer secrets as a whole. Query strings are already redacted by the HTTP instrumentation.
/// </summary>
public static partial class SecretUrlRedactor
{
    private static readonly string[] WebhookHostSuffixes = [".webhook.office.com", ".logic.azure.com", ".powerplatform.com"];

    /// <summary>The URL to record instead, or null when <paramref name="uri"/> carries no secret in its path.</summary>
    public static string? Redact(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
        {
            return null;
        }

        if (TelegramToken().IsMatch(uri.AbsolutePath))
        {
            return $"{uri.GetLeftPart(UriPartial.Authority)}{TelegramToken().Replace(uri.AbsolutePath, "/bot***/")}";
        }

        return WebhookHostSuffixes.Any(suffix => uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            ? $"{uri.GetLeftPart(UriPartial.Authority)}/***"
            : null;
    }

    // Bot tokens look like 123456789:AAH...; anchoring on that shape keeps ordinary "/bot..." path segments intact.
    [GeneratedRegex("/bot[0-9]+:[A-Za-z0-9_-]+/", RegexOptions.CultureInvariant)]
    private static partial Regex TelegramToken();
}
