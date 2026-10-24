using System.Runtime.CompilerServices;
using TenderSync.Application.Features.Notifications;
using TenderSync.Domain.Enums;

namespace TenderSync.Application.Tests.Notifications;

/// <summary>Every template family on every rich channel, snapshotted (WP10 acceptance: template snapshot tests).</summary>
public class NotificationTemplateTests
{
    private static readonly NotificationTemplates Templates = new();

    public static NotificationModel SampleModel(NotificationType type) => new()
    {
        Type = type.ToString(),
        BidNumber = "WCGHCC0038/1/2025",
        Title = "Supply & support of an electronic patient record system <v2>",
        Buyer = "Western Cape - Health",
        Province = "WesternCape",
        Closing = "Fri 20 Nov 2026 11:00 SAST",
        ClosingIn = "in 27 days",
        Relevance = 82,
        Categories = "SW_DEV, APP_SUPPORT",
        Summary = "Three-year support of the patient record system across 12 hospitals.",
        Verdict = "Partial: open questions",
        Score = 64,
        EstimatedFunctionality = 72,
        MissingItems = ["B-BBEE certificate", "SBD 6.1: Preference points claim form", "ISO 27001 certificate"],
        QuestionCount = 3,
        TenderUrl = "http://localhost:3000/tenders/01a16e25-86a8-7258-a729-e1951acbbdca",
        SourceUrl = "https://www.etenders.gov.za/Home/TenderDetails?Id=175445",
        Company = "Acme Software",
        Briefing = "Compulsory briefing Tue 3 Nov 2026 10:00 SAST, Boardroom A",
        Change = "2 new documents: Addendum 1.pdf, Pricing schedule v2.xlsx; closing date moved from Fri 20 Nov 2026 11:00 SAST to Fri 27 Nov 2026 11:00 SAST.",
        Source = "etenders",
        Message = "3 consecutive failures. Last error: HTTP 500 from /api/OCDSReleases",
        DocumentTitle = "Tax compliance PIN 2026",
        ExpiresOn = "Mon 23 Nov 2026",
        DaysLeft = 30,
        Date = "Saturday 24 October 2026",
        DashboardUrl = "http://localhost:3000",
        NewMatches = [new("WCGHCC0038/1/2025", "Patient record system", "Western Cape - Health", "Fri 20 Nov 2026 11:00 SAST", "in 27 days", 82, "Partial: open questions", "http://localhost:3000/tenders/1")],
        ClosingSoon = [new("E175322", "SAP support services", "ESKOM", "Mon 26 Oct 2026 11:00 SAST", "in 2 days", 70, "Qualify", "http://localhost:3000/tenders/2")],
        Qualified = [],
    };

    public static TheoryData<NotificationType, NotificationChannel> Cases()
    {
        var data = new TheoryData<NotificationType, NotificationChannel>();
        foreach (var type in new[] { NotificationType.NewMatch, NotificationType.QualificationReady, NotificationType.ClosingSoon3d, NotificationType.BriefingReminder, NotificationType.TenderChanged, NotificationType.AwardPublished, NotificationType.PipelineFailure, NotificationType.DocumentExpiring, NotificationType.DailyDigest, NotificationType.Test })
        {
            foreach (var channel in new[] { NotificationChannel.Email, NotificationChannel.Telegram, NotificationChannel.Teams })
            {
                data.Add(type, channel);
            }
        }

        data.Add(NotificationType.NewMatch, NotificationChannel.Sms);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Templates_render_as_snapshotted(NotificationType type, NotificationChannel channel)
    {
        var rendered = Templates.Render(type, channel, SampleModel(type));

        rendered.Subject.Should().NotBeNullOrWhiteSpace();
        rendered.Body.Should().NotBeNullOrWhiteSpace();
        return Verify($"Subject: {rendered.Subject}\n\n{rendered.Body}").UseParameters(type, channel);
    }

    [Fact]
    public void Html_channels_escape_values_and_every_family_exists_for_the_rich_channels()
    {
        var email = Templates.Render(NotificationType.NewMatch, NotificationChannel.Email, SampleModel(NotificationType.NewMatch));
        email.Body.Should().Contain("&lt;v2&gt;").And.NotContain("<v2>");
        email.Body.Should().Contain("Verify against the source documents", "the disclaimer is on every email");

        var telegram = Templates.Render(NotificationType.NewMatch, NotificationChannel.Telegram, SampleModel(NotificationType.NewMatch));
        telegram.Body.Should().Contain("&lt;v2&gt;").And.Contain("<b>WCGHCC0038/1/2025</b>");

        foreach (var type in Enum.GetValues<NotificationType>())
        {
            foreach (var channel in Enum.GetValues<NotificationChannel>())
            {
                Templates.Exists(type, channel).Should().BeTrue($"{type} on {channel} must at least fall back to a default");
            }
        }
    }

    [Fact]
    public void A_missing_family_falls_back_to_the_default_text_template()
    {
        var root = Path.Combine(Path.GetTempPath(), "tendersync-templates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "default.text.sbn"), "{{ Type }} {{ BidNumber }}\nfallback body");
        try
        {
            var templates = new NotificationTemplates(root);
            var rendered = templates.Render(NotificationType.NewMatch, NotificationChannel.Email, SampleModel(NotificationType.NewMatch));
            rendered.Subject.Should().Be("NewMatch WCGHCC0038/1/2025");
            rendered.Body.Should().Be("fallback body");
            FluentActions.Invoking(() => new NotificationTemplates(Path.Combine(root, "missing")).Render(NotificationType.Test, NotificationChannel.Email, SampleModel(NotificationType.Test)))
                .Should().Throw<InvalidOperationException>().WithMessage("*No notification template*");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

internal static class VerifySetup
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifierSettings.DontScrubDateTimes();
        VerifierSettings.DontScrubGuids();
        VerifierSettings.DisableRequireUniquePrefix();
        DiffEngine.DiffRunner.Disabled = true;
        Verifier.UseProjectRelativeDirectory("Notifications/Snapshots");
    }
}
