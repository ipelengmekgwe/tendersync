using Microsoft.AspNetCore.Http.Extensions;
using TenderSync.Worker.Extensions;

namespace TenderSync.Worker.Infrastructure;

/// <summary>
/// Lets a browser open the Hangfire dashboard with <c>/hangfire?access_token=&lt;jwt&gt;</c> once: the token moves into an
/// HttpOnly cookie and the request is redirected to the same path without the query string. The JWT handler then reads the cookie.
/// </summary>
internal sealed class DashboardTokenCookieMiddleware(RequestDelegate next)
{
    public const string CookieName = "tendersync_dashboard_token";
    public const string QueryName = "access_token";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments(WorkerAppExtensions.DashboardPath) && context.Request.Query.TryGetValue(QueryName, out var token) && !string.IsNullOrWhiteSpace(token))
        {
            context.Response.Cookies.Append(CookieName, token!, new CookieOptions
            {
                HttpOnly = true,
                // Behind a TLS-terminating ingress the request reaches the Worker as http, so IsHttps alone would drop the
                // Secure flag exactly where it matters. Only plain-http loopback (local development) goes without it.
                Secure = context.Request.IsHttps || !IsLoopback(context.Request.Host.Host),
                SameSite = SameSiteMode.Strict,
                Path = WorkerAppExtensions.DashboardPath,
                MaxAge = TimeSpan.FromHours(8),
            });

            var query = QueryString.Create(context.Request.Query.Where(q => q.Key != QueryName).SelectMany(q => q.Value.Select(v => new KeyValuePair<string, string?>(q.Key, v))));
            context.Response.Redirect(context.Request.PathBase + context.Request.Path + query);
            return;
        }

        await next(context);
    }

    private static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || (System.Net.IPAddress.TryParse(host, out var address) && System.Net.IPAddress.IsLoopback(address));
}
