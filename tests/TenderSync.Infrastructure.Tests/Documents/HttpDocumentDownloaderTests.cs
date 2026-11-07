using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Documents;
using TenderSync.Application.Features.Documents;
using TenderSync.Infrastructure.Documents;

namespace TenderSync.Infrastructure.Tests.Documents;

public class HttpDocumentDownloaderTests
{
    private static readonly Uri Url = new("https://www.etenders.gov.za/home/Download?blobName=abc.pdf&downloadedFileName=Bid%20Pack.pdf");

    private static HttpDocumentDownloader Sut(Func<HttpRequestMessage, HttpResponseMessage> respond, DocumentOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddHttpClient(HttpDocumentDownloader.ClientName).ConfigurePrimaryHttpMessageHandler(() => new DelegateHandler(respond));
        var provider = services.BuildServiceProvider();
        var opts = Options.Create(options ?? new DocumentOptions { DownloadSpacingMs = 0 });
        return new HttpDocumentDownloader(provider.GetRequiredService<IHttpClientFactory>(), new DownloadThrottle(opts), opts, NullLogger<HttpDocumentDownloader>.Instance);
    }

    [Fact]
    public async Task Streams_the_body_to_a_seekable_temp_file_with_content_type_and_file_name()
    {
        var bytes = SampleFiles.Pdf("hello");
        var sut = Sut(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "\"Bid Pack.pdf\"" };
            return response;
        });

        await using var file = await sut.DownloadAsync(Url, maxBytes: 10 * 1024 * 1024);

        file.Length.Should().Be(bytes.Length);
        file.ContentType.Should().Be("application/pdf");
        file.FileName.Should().Be("Bid Pack.pdf");
        file.Content.CanSeek.Should().BeTrue();
        file.Content.Position.Should().Be(0);
        using var buffer = new MemoryStream();
        await file.Content.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal(bytes);
    }

    [Fact]
    public async Task Non_success_status_is_a_download_error()
    {
        var sut = Sut(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { ReasonPhrase = "Not Found" });

        var act = () => sut.DownloadAsync(Url, 1024);

        (await act.Should().ThrowAsync<DocumentDownloadException>()).WithMessage("HTTP 404*");
    }

    [Fact]
    public async Task Redirects_within_the_allowed_hosts_are_followed()
    {
        var target = new Uri("https://etenders.gov.za/files/abc.pdf");
        var sut = Sut(request => request.RequestUri == Url
            ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = target } }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(SampleFiles.Pdf("moved")) });

        await using var file = await sut.DownloadAsync(Url, 10 * 1024 * 1024);

        file.Length.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("http://169.254.169.254/metadata/identity/oauth2/token")]
    [InlineData("http://localhost:5000/api/v1/admin/jobs")]
    [InlineData("https://evil.example/payload.pdf")]
    public async Task A_redirect_off_the_allowed_hosts_is_refused_before_it_is_requested(string location)
    {
        var requested = new List<Uri>();
        var sut = Sut(request =>
        {
            requested.Add(request.RequestUri!);
            return new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri(location) } };
        });

        var act = () => sut.DownloadAsync(Url, 1024);

        (await act.Should().ThrowAsync<DocumentDownloadException>()).WithMessage("Redirect to*not allowed*");
        requested.Should().Equal(Url);
    }

    [Fact]
    public void Without_an_allowlist_ip_literals_and_localhost_are_still_refused()
    {
        var open = new DocumentOptions { AllowedDownloadHosts = [] };

        open.AllowsDownloadFrom(new Uri("https://cdn.example.org/a.pdf")).Should().BeTrue();
        open.AllowsDownloadFrom(new Uri("http://10.0.0.4/a.pdf")).Should().BeFalse();
        open.AllowsDownloadFrom(new Uri("http://[::1]/a.pdf")).Should().BeFalse();
        open.AllowsDownloadFrom(new Uri("http://localhost/a.pdf")).Should().BeFalse();
        open.AllowsDownloadFrom(new Uri("ftp://cdn.example.org/a.pdf")).Should().BeFalse();
    }

    [Fact]
    public async Task Bodies_over_the_cap_are_rejected_by_header_or_while_streaming()
    {
        var big = new byte[2048];
        var withLength = Sut(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(big) });
        (await FluentActions.Awaiting(() => withLength.DownloadAsync(Url, 1024)).Should().ThrowAsync<DocumentDownloadException>()).WithMessage("*limit*");

        var chunked = Sut(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(big)) { Headers = { ContentLength = null } } });
        (await FluentActions.Awaiting(() => chunked.DownloadAsync(Url, 1024)).Should().ThrowAsync<DocumentDownloadException>()).WithMessage("*limit*");
    }

    [Fact]
    public async Task The_throttle_limits_concurrency_and_spaces_starts()
    {
        using var throttle = new DownloadThrottle(maxConcurrent: 2, spacing: TimeSpan.FromMilliseconds(120));

        var started = System.Diagnostics.Stopwatch.StartNew();
        var first = await throttle.AcquireAsync(CancellationToken.None);
        var second = await throttle.AcquireAsync(CancellationToken.None);
        started.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100), "the second start waits out the spacing");
        throttle.Available.Should().Be(0);

        var third = throttle.AcquireAsync(CancellationToken.None);
        await Task.Delay(200);
        third.IsCompleted.Should().BeFalse("two slots are taken");

        first.Dispose();
        (await third.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        second.Dispose();
        throttle.Available.Should().Be(2);
    }

    [Theory]
    [InlineData("attachment; filename=\"Bid Pack.pdf\"", "Bid Pack.pdf")]
    [InlineData("attachment; filename*=UTF-8''Annexure%20B.xlsx", "Annexure B.xlsx")]
    [InlineData("attachment", null)]
    public void Content_disposition_file_names_are_decoded(string header, string? expected)
    {
        HttpDocumentDownloader.FileNameOf(ContentDispositionHeaderValue.Parse(header)).Should().Be(expected);
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
