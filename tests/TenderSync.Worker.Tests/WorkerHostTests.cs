using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Worker.Jobs;

namespace TenderSync.Worker.Tests;

[Collection(WorkerCollection.Name)]
[Trait("Category", "Integration")]
public class WorkerHostTests(WorkerFactory factory)
{
    [Fact]
    public async Task Worker_is_live_and_ready_with_database_and_hangfire_checks()
    {
        using var client = factory.CreateClient();

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);

        var ready = await client.GetFromJsonAsync<JsonElement>("/health/ready");
        ready.GetProperty("status").GetString().Should().Be("Healthy");
        ready.GetProperty("checks").EnumerateArray().Select(c => c.GetProperty("name").GetString()).Should().Contain(["database", "hangfire"]);
    }

    [Fact]
    public async Task Hangfire_schema_exists_and_recurring_jobs_are_registered()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IReadDb>();
        var storage = factory.Services.GetRequiredService<JobStorage>();

        var tables = await db.ExecuteScalarAsync<long>("select count(*) from information_schema.tables where table_schema = 'hangfire'");
        tables.Should().BeGreaterThan(5, "Hangfire creates its own schema on first use");

        using var connection = storage.GetConnection();
        var recurring = connection.GetRecurringJobs().Select(j => j.Id).ToList();
        recurring.Should().Contain(JobSchedule.ExpectedRecurringJobIds);
    }

    [Fact]
    public void Enqueued_outbox_job_lands_on_the_notify_queue()
    {
        var client = factory.Services.GetRequiredService<IBackgroundJobClient>();
        var monitoring = factory.Services.GetRequiredService<JobStorage>().GetMonitoringApi();
        var before = monitoring.EnqueuedCount(JobQueues.Notify);

        // Enqueue is synchronous (RecurringJobManager.Trigger is not on every storage), and the server is disabled in tests,
        // so the job stays exactly where the [Queue] attribute routed it.
        var jobId = client.Enqueue<ProcessOutboxJob>(job => job.ExecuteAsync(CancellationToken.None));

        jobId.Should().NotBeNullOrEmpty();
        monitoring.EnqueuedCount(JobQueues.Notify).Should().Be(before + 1);
        monitoring.EnqueuedCount(JobQueues.Default).Should().Be(0, "nothing should fall through to the default queue");
    }

    [Fact]
    public async Task Dashboard_requires_an_admin_token_via_header_or_cookie()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/hangfire")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var viewer = await factory.CreateClientAsAsync(WorkerFactory.ViewerEmail, WorkerFactory.ViewerPassword);
        (await viewer.GetAsync("/hangfire")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var admin = await factory.CreateClientAsAsync(WorkerFactory.AdminEmail, WorkerFactory.AdminPassword);
        var page = await admin.GetAsync("/hangfire");
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        (await page.Content.ReadAsStringAsync()).Should().Contain("TenderSync jobs");

        // Browser flow: token in the query string once, then the cookie carries it.
        var token = await factory.AccessTokenForAsync(WorkerFactory.AdminEmail, WorkerFactory.AdminPassword);
        using var browser = factory.CreateClient();
        var viaCookie = await browser.GetAsync($"/hangfire?access_token={token}");
        viaCookie.StatusCode.Should().Be(HttpStatusCode.OK);
        viaCookie.RequestMessage!.RequestUri!.Query.Should().BeEmpty("the redirect strips the token from the URL");
    }
}
