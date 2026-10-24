using System.Net;
using System.Net.Http.Json;
using TenderSync.Application.Common.Auditing;
using TenderSync.Application.Common.Models;
using TenderSync.Application.Features.Admin.Queries.ListAuditLogs;

namespace TenderSync.Api.Tests;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class AdminAndAuditTests(ApiFactory factory)
{
    [Fact]
    public async Task A_viewer_is_forbidden_from_admin_endpoints()
    {
        using var client = await factory.CreateViewerClientAsync();

        (await client.GetAsync("/api/v1/admin/audit-logs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_can_read_source_health_and_job_runs()
    {
        using var admin = await factory.CreateAdminClientAsync();

        var health = await admin.GetAsync("/api/v1/admin/source-health?take=10");
        health.StatusCode.Should().Be(HttpStatusCode.OK);

        var runs = await admin.GetAsync("/api/v1/admin/jobs/runs?pageSize=10");
        runs.StatusCode.Should().Be(HttpStatusCode.OK);

        using var viewer = await factory.CreateViewerClientAsync();
        (await viewer.GetAsync("/api/v1/admin/source-health")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Successful_commands_write_audit_rows_with_sensitive_fields_redacted()
    {
        await factory.LoginAsync(ApiFactory.ViewerEmail, ApiFactory.ViewerPassword);
        using var admin = await factory.CreateAdminClientAsync();

        var page = await admin.GetFromJsonAsync<PagedResult<AuditLogDto>>("/api/v1/admin/audit-logs?action=LoginCommand&pageSize=50");

        page!.Items.Should().NotBeEmpty();
        var entry = page.Items.First(e => e.AfterJson!.Contains(ApiFactory.ViewerEmail, StringComparison.Ordinal));
        entry.Action.Should().Be("LoginCommand");
        entry.ActorName.Should().Be("anonymous", "login is sent before the caller has an identity");
        // jsonb normalises whitespace, so compare parsed values rather than raw text.
        var payload = System.Text.Json.JsonDocument.Parse(entry.AfterJson!).RootElement;
        payload.GetProperty("password").GetString().Should().Be(RequestRedactor.Redacted);
        payload.GetProperty("email").GetString().Should().Be(ApiFactory.ViewerEmail);
        entry.AfterJson.Should().NotContain(ApiFactory.ViewerPassword);
    }
}
