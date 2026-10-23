using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TenderSync.Api.Tests;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class HostTests(ApiFactory factory)
{
    [Fact]
    public async Task Liveness_and_readiness_report_healthy()
    {
        using var client = factory.CreateClient();

        var live = await client.GetAsync("/health/live");
        live.StatusCode.Should().Be(HttpStatusCode.OK);

        var ready = await client.GetFromJsonAsync<JsonElement>("/health/ready");
        ready.GetProperty("status").GetString().Should().Be("Healthy");
        ready.GetProperty("checks").EnumerateArray().Should().Contain(c => c.GetProperty("name").GetString() == "database");
    }

    [Fact]
    public async Task OpenApi_document_declares_the_bearer_scheme_and_the_endpoints()
    {
        using var client = factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");

        document.GetProperty("info").GetProperty("title").GetString().Should().Be("TenderSync API");
        document.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _).Should().BeTrue();
        var paths = document.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        paths.Should().Contain(["/api/v1/auth/login", "/api/v1/auth/refresh", "/api/v1/auth/me", "/api/v1/tenders", "/api/v1/tenders/{id}", "/api/v1/admin/audit-logs"]);
    }

    [Fact]
    public async Task Scalar_reference_page_renders()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/scalar/v1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
    }

    [Fact]
    public async Task Unknown_routes_return_problem_details_not_html()
    {
        using var client = await factory.CreateViewerClientAsync();

        var response = await client.GetAsync("/api/v1/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
