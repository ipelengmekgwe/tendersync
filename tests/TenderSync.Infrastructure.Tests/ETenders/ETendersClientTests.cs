using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
using TenderSync.Infrastructure.ETenders;

namespace TenderSync.Infrastructure.Tests.ETenders;

public class ETendersClientTests
{
    private static readonly DateOnly From = new(2026, 10, 15);
    private static readonly DateOnly To = new(2026, 10, 23);

    [Fact]
    public async Task Follows_links_next_until_the_terminal_page()
    {
        var handler = ScriptedHandler.TwoPages();
        await using var provider = ETendersTestServices.Build(handler);
        var client = provider.GetRequiredService<ETendersClient>();

        var pages = new List<OcdsPage>();
        await foreach (var page in client.GetPagesAsync(From, To))
        {
            pages.Add(page);
        }

        pages.Should().HaveCount(2);
        pages[0].Releases.Should().HaveCount(100);
        pages[0].NextUrl.Should().Contain("PageNumber=2");
        pages[1].Releases.Should().HaveCount(3);
        pages[1].NextUrl.Should().BeNull();
        handler.Requests.Should().AllSatisfy(u => u.Query.Should().Contain("PageSize=100").And.Contain("dateFrom=2026-10-15").And.Contain("dateTo=2026-10-23"));
    }

    [Fact]
    public async Task Parses_the_release_envelope_with_raw_json()
    {
        var page = ETendersClient.ParsePage(1, Fixtures.Page1());

        var first = page.Releases[0];
        first.Release.Ocid.Should().StartWith("ocds-9t57fa-");
        first.Release.Tender!.Documents.Should().NotBeEmpty();
        first.RawJson.Should().Contain(first.Release.Ocid!);
        page.Releases.Select(r => r.Release.Tag.Single()).Should().AllBe("compiled");
    }

    [Fact]
    public async Task Retries_transient_failures_then_succeeds()
    {
        var handler = new ScriptedHandler((request, call) => call <= 2 ? ScriptedHandler.Status(HttpStatusCode.ServiceUnavailable) : ScriptedHandler.Json(Fixtures.Page2Terminal()));
        await using var provider = ETendersTestServices.Build(handler);
        var client = provider.GetRequiredService<ETendersClient>();

        var page = await client.GetPageAsync(From, To, 1);

        page.Releases.Should().HaveCount(3);
        handler.Calls.Should().Be(3, "two 503s then a 200");
    }

    [Fact]
    public async Task Gives_up_after_the_retry_budget()
    {
        var handler = new ScriptedHandler((_, _) => ScriptedHandler.Status(HttpStatusCode.BadGateway));
        await using var provider = ETendersTestServices.Build(handler, o => o.CircuitBreakerMinimumThroughput = 100);
        var client = provider.GetRequiredService<ETendersClient>();

        var act = () => client.GetPageAsync(From, To, 1);

        await act.Should().ThrowAsync<HttpRequestException>();
        handler.Calls.Should().Be(4, "one attempt plus three retries");
    }

    [Fact]
    public async Task A_plain_500_is_not_retried_because_the_api_returns_it_for_poison_rows()
    {
        var handler = new ScriptedHandler((_, _) => ScriptedHandler.Status(HttpStatusCode.InternalServerError));
        await using var provider = ETendersTestServices.Build(handler);
        var client = provider.GetRequiredService<ETendersClient>();

        var act = () => client.GetPageAsync(From, To, 1);

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Opens_the_circuit_after_repeated_failures()
    {
        var handler = new ScriptedHandler((_, _) => ScriptedHandler.Status(HttpStatusCode.ServiceUnavailable));
        await using var provider = ETendersTestServices.Build(handler, o => { o.RetryCount = 1; o.CircuitBreakerMinimumThroughput = 3; });
        var client = provider.GetRequiredService<ETendersClient>();

        // Each call makes two attempts (one retry); the breaker opens once three failures are sampled, so the
        // BrokenCircuitException surfaces on the second or third call.
        var opened = false;
        for (var call = 0; call < 4 && !opened; call++)
        {
            try
            {
                await client.GetPageAsync(From, To, 1);
            }
            catch (BrokenCircuitException)
            {
                opened = true;
            }
            catch (HttpRequestException)
            {
                // expected while the circuit is still closed
            }
        }

        opened.Should().BeTrue("repeated 503s must open the circuit");
        var callsBefore = handler.Calls;
        await FluentActions.Awaiting(() => client.GetPageAsync(From, To, 1)).Should().ThrowAsync<BrokenCircuitException>();
        handler.Calls.Should().Be(callsBefore, "an open circuit fails fast without calling eTenders");
    }

    [Fact]
    public async Task Release_by_ocid_returns_null_on_404()
    {
        var handler = new ScriptedHandler((_, _) => ScriptedHandler.Status(HttpStatusCode.NotFound));
        await using var provider = ETendersTestServices.Build(handler);
        var client = provider.GetRequiredService<ETendersClient>();

        (await client.GetReleaseAsync("ocds-9t57fa-000000")).Should().BeNull();
    }
}
