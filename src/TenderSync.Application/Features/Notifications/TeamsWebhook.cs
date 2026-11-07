namespace TenderSync.Application.Features.Notifications;

/// <summary>
/// Where a Teams "address" may point. The address is a URL the Worker POSTs to, so without a host check any user could
/// aim it at an internal service. Only Microsoft's webhook hosts qualify: Teams incoming webhooks and the Power Automate
/// (Workflows) URLs that replace them.
/// </summary>
public static class TeamsWebhook
{
    public static readonly IReadOnlyList<string> HostSuffixes = [".webhook.office.com", ".logic.azure.com", ".powerplatform.com"];

    public static bool IsAllowed(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && IsAllowed(uri);

    public static bool IsAllowed(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IsAbsoluteUri
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.IsDefaultPort
            && HostSuffixes.Any(suffix => uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }
}
