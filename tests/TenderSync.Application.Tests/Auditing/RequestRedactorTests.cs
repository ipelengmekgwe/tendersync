using System.Text.Json;
using TenderSync.Application.Common.Auditing;
using TenderSync.Application.Features.Companies;
using TenderSync.Application.Features.Companies.Commands.UpdateCompanyProfile;
using TenderSync.Application.Features.Companies.Commands.UploadCompanyDocument;
using TenderSync.Application.Features.Notifications;
using TenderSync.Application.Features.Notifications.Commands.UpdateNotificationPreferences;
using TenderSync.Domain.Enums;

namespace TenderSync.Application.Tests.Auditing;

public class RequestRedactorTests
{
    [Fact]
    public void Sensitive_properties_are_redacted_inside_nested_inputs_and_streams_are_never_serialised()
    {
        var update = new UpdateCompanyProfileCommand(Guid.CreateVersion7(), new CompanyProfileInput("Acme", TaxPin: "SECRET-PIN", CsdNumber: "MAAA1"));
        using var upload = JsonDocument.Parse(RequestRedactor.ToJson(new UploadCompanyDocumentCommand(Guid.CreateVersion7(), CompanyDocumentType.Cv, "CV", "cv.pdf", "application/pdf", 10, null, new MemoryStream(new byte[10]))));
        using var json = JsonDocument.Parse(RequestRedactor.ToJson(update));

        var profile = json.RootElement.GetProperty("profile");
        profile.GetProperty("taxPin").GetString().Should().Be(RequestRedactor.Redacted);
        profile.GetProperty("csdNumber").GetString().Should().Be("MAAA1");
        profile.GetProperty("name").GetString().Should().Be("Acme");
        upload.RootElement.GetProperty("content").GetString().Should().Be(RequestRedactor.Redacted);
        upload.RootElement.GetProperty("title").GetString().Should().Be("CV");

        using var list = JsonDocument.Parse(RequestRedactor.ToJson(new UpdateNotificationPreferencesCommand([new NotificationPreferenceInput(NotificationChannel.Email, "jane@example.com", [NotificationType.NewMatch])])));
        var first = list.RootElement.GetProperty("preferences")[0];
        first.GetProperty("address").GetString().Should().Be(RequestRedactor.Redacted, "items of a list are walked too");
        first.GetProperty("types")[0].GetString().Should().Be("NewMatch");
    }

    [Fact]
    public void Objects_nested_deeper_than_the_walk_are_replaced_rather_than_serialised_in_full()
    {
        var deep = new Level(new Level(new Level(new Level(new Level(new Level(new Level(null, "level 6", "hunter2"), "level 5"), "level 4"), "level 3"), "level 2"), "level 1"), "level 0");

        var text = RequestRedactor.ToJson(deep);
        using var json = JsonDocument.Parse(text);

        var node = json.RootElement;
        for (var i = 0; i < 6; i++)
        {
            node.GetProperty("label").GetString().Should().Be($"level {i}");
            node = node.GetProperty("inner");
        }

        node.GetString().Should().Be(RequestRedactor.Redacted, "the seventh level could carry a secret the walk never inspected");
        text.Should().NotContain("hunter2");
    }

    private sealed record Level(Level? Inner, string Label, [property: Sensitive] string? Secret = null);
}
