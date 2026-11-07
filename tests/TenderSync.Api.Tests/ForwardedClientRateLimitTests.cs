using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TenderSync.Api.Tests;

/// <summary>
/// Every call reaches the API from the dashboard's server, so the sign-in limit must be counted per browser address taken
/// from X-Forwarded-For, not per dashboard container. The test server plays the dashboard at a private address.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class ForwardedClientRateLimitTests(ApiFactory factory)
{
    private WebApplicationFactory<Program> LimitedTo(int permits) => factory.WithWebHostBuilder(builder =>
    {
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:Auth:PermitLimit"] = permits.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }));
        builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new PrivatePeerStartupFilter()));
    });

    private static async Task<HttpStatusCode> LoginFrom(HttpClient client, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            // An unknown account: a wrong password for a real one would count towards its lockout.
            Content = JsonContent.Create(new { email = "nobody@tendersync.test", password = "not-a-real-password" }),
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task Sign_in_attempts_are_limited_per_forwarded_client_address()
    {
        using var host = LimitedTo(2);
        using var client = host.CreateClient();

        (await LoginFrom(client, "203.0.113.10")).Should().Be(HttpStatusCode.Unauthorized);
        (await LoginFrom(client, "203.0.113.10")).Should().Be(HttpStatusCode.Unauthorized);
        (await LoginFrom(client, "203.0.113.10")).Should().Be(HttpStatusCode.TooManyRequests, "that browser used up its attempts");

        (await LoginFrom(client, "203.0.113.20")).Should().Be(HttpStatusCode.Unauthorized, "another browser behind the same dashboard keeps its own allowance");
    }

    [Fact]
    public async Task An_address_the_client_wrote_itself_cannot_escape_the_limit()
    {
        using var host = LimitedTo(1);
        using var client = host.CreateClient();

        // The ingress appends the real address, so a forged hop sits to its left and is never trusted.
        (await LoginFrom(client, "198.51.100.1, 203.0.113.30")).Should().Be(HttpStatusCode.Unauthorized);
        (await LoginFrom(client, "198.51.100.2, 203.0.113.30")).Should().Be(HttpStatusCode.TooManyRequests);
    }

    /// <summary>The test server has no peer address; give every request the dashboard's private one.</summary>
    private sealed class PrivatePeerStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
