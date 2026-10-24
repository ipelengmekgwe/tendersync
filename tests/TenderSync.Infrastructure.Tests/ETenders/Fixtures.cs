using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TenderSync.Infrastructure.ETenders;

namespace TenderSync.Infrastructure.Tests.ETenders;

/// <summary>Recorded eTenders responses from docs/research/etenders-samples (copied to the test output as Fixtures/).</summary>
public static class Fixtures
{
    public const string Page1File = "releases-2026-10-23-page1-size100.json";
    public const string Page2File = "releases-size3.json";

    public static string Read(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", file));

    public static string Page1() => Read(Page1File);

    /// <summary>The 3-row sample as a terminal page: links.next removed so paging stops.</summary>
    public static string Page2Terminal()
    {
        var node = JsonNode.Parse(Read(Page2File))!;
        node["links"] = new JsonObject { ["next"] = null };
        return node.ToJsonString();
    }

    /// <summary>Page 1 with one release's closing date moved, to simulate a changed release.</summary>
    public static (string Json, string Ocid, DateTimeOffset NewClosing) Page1WithMovedClosing()
    {
        var node = JsonNode.Parse(Page1())!;
        var release = node["releases"]![0]!;
        var ocid = release["ocid"]!.GetValue<string>();
        var newClosing = new DateTimeOffset(2026, 12, 15, 11, 0, 0, TimeSpan.Zero);
        release["tender"]!["tenderPeriod"]!["endDate"] = newClosing.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        return (node.ToJsonString(), ocid, newClosing);
    }

    public static int DistinctOcidsInPage1() =>
        JsonNode.Parse(Page1())!["releases"]!.AsArray().Select(r => r!["ocid"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count();
}

/// <summary>Scripted HTTP responses for the eTenders client, keyed by page number.</summary>
public sealed class ScriptedHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
{
    private int _calls;

    public int Calls => _calls;

    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref _calls);
        lock (Requests)
        {
            Requests.Add(request.RequestUri!);
        }

        return Task.FromResult(respond(request, call));
    }

    public static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status) { Content = new StringContent(string.Empty) };

    public static int PageNumber(HttpRequestMessage request)
    {
        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
        return int.TryParse(query["PageNumber"], out var page) ? page : 1;
    }

    /// <summary>Page 1 → the 100-row sample (its links.next points to page 2), any other page → the terminal 3-row sample.</summary>
    public static ScriptedHandler TwoPages() => new((request, _) => Json(PageNumber(request) == 1 ? Fixtures.Page1() : Fixtures.Page2Terminal()));
}

public static class ETendersTestServices
{
    /// <summary>The production registration (AddETenders) with the primary handler swapped for a script and fast resilience settings.</summary>
    public static ServiceProvider Build(HttpMessageHandler handler, Action<ETendersOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddETenders();
        services.Configure<ETendersOptions>(options =>
        {
            options.RetryCount = 3;
            options.RetryBaseDelaySeconds = 0.01;
            options.CircuitBreakerMinimumThroughput = 3;
            options.CircuitBreakerSamplingSeconds = 30;
            options.CircuitBreakerBreakSeconds = 30;
            options.AttemptTimeoutSeconds = 5;
            options.TotalTimeoutSeconds = 20;
            configure?.Invoke(options);
        });
        services.AddHttpClient<ETendersClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }
}
