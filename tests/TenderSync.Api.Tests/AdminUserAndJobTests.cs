using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions.Identity;
using TenderSync.Application.Abstractions.Jobs;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Features.Admin.Commands.RunJobNow;
using TenderSync.Application.Features.Applications.Queries.ListApplications;
using TenderSync.Application.Security;

namespace TenderSync.Api.Tests;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class AdminUserAndJobTests(ApiFactory factory)
{
    private const string Password = "Long-Enough-Password-1";

    private static string NewEmail() => $"user{Random.Shared.Next(100000, 999999)}@tendersync.test";

    [Fact]
    public async Task Admin_creates_a_user_who_can_sign_in_with_that_role()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var email = NewEmail();

        var response = await admin.PostAsJsonAsync("/api/v1/admin/users", new { email, displayName = "New Reviewer", role = Roles.Reviewer, password = Password });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<UserAccount>())!;
        created.Should().Match<UserAccount>(u => u.Email == email && u.Roles.Single() == Roles.Reviewer && !u.Disabled);

        (await admin.PostAsJsonAsync("/api/v1/admin/users", new { email, displayName = "Again", role = Roles.Viewer, password = Password })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsJsonAsync("/api/v1/admin/users", new { email = NewEmail(), displayName = "Weak", role = Roles.Viewer, password = "short" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/api/v1/admin/users", new { email = NewEmail(), displayName = "Bad role", role = "Owner", password = Password })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var users = await admin.GetFromJsonAsync<List<UserAccount>>("/api/v1/admin/users");
        users!.Should().Contain(u => u.Id == created.Id).And.Contain(u => u.Email == ApiFactory.AdminEmail && u.Roles.Contains(Roles.Admin));

        using var reviewer = await factory.CreateClientAsAsync(email, Password);
        (await reviewer.GetAsync("/api/v1/applications")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await reviewer.GetAsync("/api/v1/admin/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Disabling_a_user_blocks_sign_in_and_refresh_and_a_new_password_works_after_re_enabling()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var email = NewEmail();
        var created = (await (await admin.PostAsJsonAsync("/api/v1/admin/users", new { email, displayName = "Soon disabled", role = Roles.Viewer, password = Password })).Content.ReadFromJsonAsync<UserAccount>())!;
        var session = await factory.LoginAsync(email, Password);

        var disabled = await (await admin.PutAsJsonAsync($"/api/v1/admin/users/{created.Id}", new { displayName = "Disabled", role = Roles.Viewer, disabled = true, newPassword = (string?)null })).Content.ReadFromJsonAsync<UserAccount>();
        disabled!.Disabled.Should().BeTrue();

        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = session.RefreshToken })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var enableResponse = await admin.PutAsJsonAsync($"/api/v1/admin/users/{created.Id}", new { displayName = "Back", role = Roles.Reviewer, disabled = false, newPassword = "Another-Long-Password-2" });
        enableResponse.StatusCode.Should().Be(HttpStatusCode.OK, await enableResponse.Content.ReadAsStringAsync());
        var enabled = await enableResponse.Content.ReadFromJsonAsync<UserAccount>();
        enabled!.Should().Match<UserAccount>(u => !u.Disabled && u.DisplayName == "Back" && u.Roles.Single() == Roles.Reviewer);
        (await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Another-Long-Password-2" })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_admin_cannot_disable_or_demote_themselves()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var me = (await admin.GetFromJsonAsync<List<UserAccount>>("/api/v1/admin/users"))!.Single(u => u.Email == ApiFactory.AdminEmail);

        (await admin.PutAsJsonAsync($"/api/v1/admin/users/{me.Id}", new { displayName = me.DisplayName, role = Roles.Admin, disabled = true, newPassword = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PutAsJsonAsync($"/api/v1/admin/users/{me.Id}", new { displayName = me.DisplayName, role = Roles.Viewer, disabled = false, newPassword = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PutAsJsonAsync($"/api/v1/admin/users/{Guid.CreateVersion7()}", new { displayName = "x", role = Roles.Viewer, disabled = false, newPassword = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Recurring_jobs_are_read_from_the_job_store_and_run_now_goes_through_the_outbox()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var readDb = scope.ServiceProvider.GetRequiredService<IReadDb>();
            await readDb.ExecuteScalarAsync<int>("""
                create schema if not exists hangfire;
                create table if not exists hangfire.set (key text not null, value text not null, score double precision not null default 0);
                create table if not exists hangfire.hash (key text not null, field text not null, value text);
                create table if not exists hangfire.job (id bigint primary key, statename text);
                delete from hangfire.set where key = 'recurring-jobs' and value = 'ingest-recent';
                delete from hangfire.hash where key = 'recurring-job:ingest-recent';
                delete from hangfire.job where id = 4242;
                insert into hangfire.set (key, value) values ('recurring-jobs', 'ingest-recent');
                insert into hangfire.hash (key, field, value) values
                    ('recurring-job:ingest-recent', 'Cron', '*/30 * * * *'),
                    ('recurring-job:ingest-recent', 'TimeZoneId', 'Africa/Johannesburg'),
                    ('recurring-job:ingest-recent', 'Queue', 'ingest'),
                    ('recurring-job:ingest-recent', 'NextExecution', '1792800000000'),
                    ('recurring-job:ingest-recent', 'LastExecution', '2026-10-24T23:27:17.0000000Z'),
                    ('recurring-job:ingest-recent', 'LastJobId', '4242');
                insert into hangfire.job (id, statename) values (4242, 'Succeeded');
                select 1;
                """);
        }

        using var admin = await factory.CreateAdminClientAsync();
        var jobs = await admin.GetFromJsonAsync<List<RecurringJobInfo>>("/api/v1/admin/jobs");
        jobs!.Should().ContainSingle(j => j.Id == "ingest-recent").Which.Should().Match<RecurringJobInfo>(j =>
            j.Cron == "*/30 * * * *" && j.Queue == "ingest" && j.LastJobState == "Succeeded"
            && j.NextExecution == DateTimeOffset.FromUnixTimeMilliseconds(1792800000000) && j.LastExecution == new DateTimeOffset(2026, 10, 24, 23, 27, 17, TimeSpan.Zero));

        var run = await admin.PostAsync("/api/v1/admin/jobs/ingest-recent/run", null);
        run.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await run.Content.ReadFromJsonAsync<RunJobNowResponse>())!.JobId.Should().Be("ingest-recent");
        (await admin.PostAsync("/api/v1/admin/jobs/no-such-job/run", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using (var scope = factory.Services.CreateScope())
        {
            var readDb = scope.ServiceProvider.GetRequiredService<IReadDb>();
            (await readDb.ExecuteScalarAsync<long>("select count(*) from tendersync.outbox_messages where type like '%JobRunRequested%' and payload_json::text like '%ingest-recent%'"))
                .Should().BeGreaterThanOrEqualTo(1);
        }

        using var viewer = await factory.CreateViewerClientAsync();
        (await viewer.PostAsync("/api/v1/admin/jobs/ingest-recent/run", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Applications_list_is_for_reviewers_and_empty_in_v1()
    {
        using var admin = await factory.CreateAdminClientAsync();
        (await admin.GetFromJsonAsync<List<ApplicationListItemDto>>("/api/v1/applications")).Should().BeEmpty();

        using var viewer = await factory.CreateViewerClientAsync();
        (await viewer.GetAsync("/api/v1/applications")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
