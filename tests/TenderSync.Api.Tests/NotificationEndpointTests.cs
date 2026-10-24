using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Common.Auditing;
using TenderSync.Application.Common.Models;
using TenderSync.Application.Features.Admin.Queries.ListAuditLogs;
using TenderSync.Application.Features.Notifications;
using TenderSync.Application.Features.Notifications.Commands.SendTestNotification;
using TenderSync.Domain.Enums;
using TenderSync.Infrastructure.Persistence;

namespace TenderSync.Api.Tests;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class NotificationEndpointTests(ApiFactory factory)
{
    // The API serialises enums as numbers, so requests and responses use the default web options.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Preferences_are_per_user_validated_and_redacted_in_the_audit_log()
    {
        using var viewer = await factory.CreateViewerClientAsync();
        using var admin = await factory.CreateAdminClientAsync();

        (await viewer.GetFromJsonAsync<List<NotificationPreferenceDto>>("/api/v1/notification-preferences", Json))!.Should().BeEmpty();

        var saved = await viewer.PutAsJsonAsync("/api/v1/notification-preferences", new[]
        {
            new NotificationPreferenceInput(NotificationChannel.Email, "viewer@tendersync.test", [NotificationType.NewMatch, NotificationType.QualificationReady], true, new QuietHoursInput("22:00", "06:00")),
            new NotificationPreferenceInput(NotificationChannel.Telegram, "123456", [NotificationType.NewMatch]),
        }, Json);
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var preferences = (await saved.Content.ReadFromJsonAsync<List<NotificationPreferenceDto>>(Json))!;
        preferences.Should().HaveCount(2);
        preferences.Single(p => p.Channel == NotificationChannel.Email).QuietHours.Should().Be(new QuietHoursInput("22:00", "06:00", "Africa/Johannesburg"));

        (await admin.GetFromJsonAsync<List<NotificationPreferenceDto>>("/api/v1/notification-preferences", Json))!.Should().BeEmpty("preferences belong to the caller");

        var replaced = await viewer.PutAsJsonAsync("/api/v1/notification-preferences", new[] { new NotificationPreferenceInput(NotificationChannel.Email, "viewer@tendersync.test", [NotificationType.DailyDigest], false) }, Json);
        var remaining = (await replaced.Content.ReadFromJsonAsync<List<NotificationPreferenceDto>>(Json))!;
        remaining.Should().ContainSingle(p => p.Channel == NotificationChannel.Email && !p.Enabled && p.Types.SequenceEqual(new[] { NotificationType.DailyDigest }), "channels left out are removed");

        var invalid = await viewer.PutAsJsonAsync("/api/v1/notification-preferences", new[]
        {
            new NotificationPreferenceInput(NotificationChannel.Email, "not-an-email", [NotificationType.NewMatch]),
            new NotificationPreferenceInput(NotificationChannel.Teams, "http://insecure.example", [NotificationType.NewMatch]),
            new NotificationPreferenceInput(NotificationChannel.Sms, "0821234567", [NotificationType.NewMatch]),
        }, Json);
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await invalid.Content.ReadAsStringAsync();
        problem.Should().Contain("Email address").And.Contain("webhook").And.Contain("E.164");

        var audit = await admin.GetFromJsonAsync<PagedResult<AuditLogDto>>("/api/v1/admin/audit-logs?action=UpdateNotificationPreferencesCommand&pageSize=50");
        var entry = audit!.Items[0];
        using var payload = JsonDocument.Parse(entry.AfterJson!);
        payload.RootElement.GetProperty("preferences")[0].GetProperty("address").GetString().Should().Be(RequestRedactor.Redacted);
        entry.AfterJson.Should().NotContain("viewer@tendersync.test");
    }

    [Fact]
    public async Task A_test_message_is_queued_for_the_callers_channel_and_listed_with_a_masked_recipient()
    {
        using var admin = await factory.CreateAdminClientAsync();
        (await admin.PostAsJsonAsync("/api/v1/notifications/test", new { channel = (int)NotificationChannel.Telegram })).StatusCode.Should().Be(HttpStatusCode.NotFound, "no Telegram preference yet");

        (await admin.PutAsJsonAsync("/api/v1/notification-preferences", new[] { new NotificationPreferenceInput(NotificationChannel.Telegram, "987654", [NotificationType.Test]) }, Json)).EnsureSuccessStatusCode();

        var response = await admin.PostAsJsonAsync("/api/v1/notifications/test", new { channel = (int)NotificationChannel.Telegram });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        var queued = (await response.Content.ReadFromJsonAsync<TestNotificationResponse>(Json))!;
        queued.Channel.Should().Be(NotificationChannel.Telegram);
        queued.Recipient.Should().Be("***7654");
        queued.Subject.Should().Be("TenderSync test message");

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await context.NotificationSet.AsNoTracking().SingleAsync(n => n.Id == queued.NotificationId);
            row.Status.Should().Be(NotificationStatus.Pending);
            row.Recipient.Should().Be("987654");
            row.Body.Should().Contain("<b>TenderSync test</b>");
        }

        var page = await admin.GetFromJsonAsync<PagedResult<NotificationDto>>($"/api/v1/notifications?type={(int)NotificationType.Test}", Json);
        page!.Items.Should().Contain(n => n.Id == queued.NotificationId && n.Recipient == "***7654" && n.Status == NotificationStatus.Pending);

        using var viewer = await factory.CreateViewerClientAsync();
        (await viewer.GetFromJsonAsync<PagedResult<NotificationDto>>($"/api/v1/notifications?type={(int)NotificationType.Test}", Json))!.Items.Should().NotContain(n => n.Id == queued.NotificationId, "another user's address");
    }
}
