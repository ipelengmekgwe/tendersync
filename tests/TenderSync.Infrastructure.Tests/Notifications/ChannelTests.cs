using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Notifications;
using TenderSync.Infrastructure.Notifications;

namespace TenderSync.Infrastructure.Tests.Notifications;

/// <summary>Payload shapes and failure handling of every channel without any real provider.</summary>
public class ChannelTests
{
    private static readonly NotificationMessage Message = new("recipient", "New match: WCGHCC0038/1/2025", "<b>WCGHCC0038/1/2025</b> — Patient record system\nClosing Fri 20 Nov 2026", "http://localhost:3000/tenders/1", "Open tender");

    private static IHttpClientFactory Factory(Func<HttpRequestMessage, HttpResponseMessage> respond, List<CapturedRequest> requests)
    {
        var services = new ServiceCollection();
        foreach (var name in new[] { TelegramChannel.ClientName, TeamsChannel.ClientName, ClickatellSmsSender.ClientName })
        {
            services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => new StubHandler(respond, requests));
        }

        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    private static NotificationChannelOptions Options(Action<NotificationChannelOptions>? configure = null)
    {
        var options = new NotificationChannelOptions();
        configure?.Invoke(options);
        return options;
    }

    [Fact]
    public async Task Email_builds_a_multipart_message_with_a_text_alternative_and_skips_when_unconfigured()
    {
        var email = new NotificationChannelOptions.EmailOptions { SmtpHost = "smtp.example.com", FromAddress = "alerts@example.com", FromName = "TenderSync" };
        var message = Message with { Recipient = "jane@example.com", Body = "<h2>WCGHCC0038/1/2025: Patient &amp; record</h2><p>Closing <strong>Fri</strong></p><p><a href=\"https://etenders.gov.za/x\">Source</a></p>" };

        using var mime = EmailChannel.Build(message, email);

        mime.From.ToString().Should().Contain("alerts@example.com");
        mime.To.ToString().Should().Be("jane@example.com");
        mime.Subject.Should().Be(message.Subject);
        mime.HtmlBody.Should().Contain("<h2>").And.Contain("http://localhost:3000/tenders/1", "the action link is appended when the body lacks it");
        mime.TextBody.Should().Contain("WCGHCC0038/1/2025: Patient & record").And.Contain("Closing Fri").And.Contain("Source (https://etenders.gov.za/x)").And.NotContain("<");

        var channel = new EmailChannel(Microsoft.Extensions.Options.Options.Create(Options()), NullLogger<EmailChannel>.Instance);
        var result = await channel.SendAsync(message);
        result.Skipped.Should().BeTrue();
        result.Error.Should().Contain("not configured");
    }

    [Fact]
    public async Task Telegram_posts_html_with_an_inline_button_and_reports_api_errors()
    {
        var requests = new List<CapturedRequest>();
        var factory = Factory(_ => Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":{\"message_id\":42}}"), requests);
        var channel = new TelegramChannel(factory, Microsoft.Extensions.Options.Options.Create(Options(o => o.Telegram.BotToken = "123:abc")), NullLogger<TelegramChannel>.Instance);

        var result = await channel.SendAsync(Message with { Recipient = "987654" });

        result.Success.Should().BeTrue();
        result.ProviderMessageId.Should().Be("42");
        requests.Single().Url.Should().Be("https://api.telegram.org/bot123:abc/sendMessage");
        using var payload = JsonDocument.Parse(requests.Single().Body);
        payload.RootElement.GetProperty("chat_id").GetString().Should().Be("987654");
        payload.RootElement.GetProperty("parse_mode").GetString().Should().Be("HTML");
        payload.RootElement.GetProperty("text").GetString().Should().StartWith("<b>WCGHCC0038/1/2025</b>");
        payload.RootElement.GetProperty("reply_markup").GetProperty("inline_keyboard")[0][0].GetProperty("url").GetString().Should().Be("http://localhost:3000/tenders/1");

        var failing = new TelegramChannel(Factory(_ => Json(HttpStatusCode.BadRequest, "{\"ok\":false,\"description\":\"Bad Request: chat not found\"}"), []), Microsoft.Extensions.Options.Options.Create(Options(o => o.Telegram.BotToken = "123:abc")), NullLogger<TelegramChannel>.Instance);
        var failed = await failing.SendAsync(Message);
        failed.Success.Should().BeFalse();
        failed.Error.Should().Contain("chat not found");

        var unconfigured = new TelegramChannel(factory, Microsoft.Extensions.Options.Options.Create(Options()), NullLogger<TelegramChannel>.Instance);
        (await unconfigured.SendAsync(Message)).Skipped.Should().BeTrue();

        var longText = new string('x', 5000);
        var truncated = JsonSerializer.SerializeToElement(TelegramChannel.BuildPayload(Message with { Body = longText })).GetProperty("text").GetString();
        truncated!.Length.Should().Be(TelegramChannel.MaxTextLength);
    }

    [Fact]
    public async Task Teams_posts_an_adaptive_card_to_the_webhook_and_rejects_non_https_recipients()
    {
        var requests = new List<CapturedRequest>();
        var channel = new TeamsChannel(Factory(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("1") }, requests), NullLogger<TeamsChannel>.Instance);

        var result = await channel.SendAsync(Message with { Recipient = "https://example.webhook.office.com/webhookb2/abc" });

        result.Success.Should().BeTrue();
        requests.Single().Url.Should().Be("https://example.webhook.office.com/webhookb2/abc");
        using var payload = JsonDocument.Parse(requests.Single().Body);
        var card = payload.RootElement.GetProperty("attachments")[0];
        card.GetProperty("contentType").GetString().Should().Be("application/vnd.microsoft.card.adaptive");
        card.GetProperty("content").GetProperty("type").GetString().Should().Be("AdaptiveCard");
        card.GetProperty("content").GetProperty("body")[0].GetProperty("text").GetString().Should().Be(Message.Subject);
        card.GetProperty("content").GetProperty("actions")[0].GetProperty("url").GetString().Should().Be("http://localhost:3000/tenders/1");

        (await channel.SendAsync(Message with { Recipient = "http://insecure" })).Error.Should().Contain("https");
        var failing = new TeamsChannel(Factory(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("Webhook message deliver failed") }, []), NullLogger<TeamsChannel>.Instance);
        (await failing.SendAsync(Message with { Recipient = "https://example.webhook.office.com/x" })).Error.Should().Contain("400");
    }

    [Fact]
    public async Task Sms_and_whatsapp_skip_with_the_no_op_senders_and_clickatell_shapes_its_request()
    {
        (await new SmsChannel(new NoOpSmsSender()).SendAsync(Message)).Skipped.Should().BeTrue();
        (await new WhatsAppChannel(new NoOpWhatsAppSender()).SendAsync(Message)).Skipped.Should().BeTrue();
        SmsChannel.Shorten(Message with { Body = new string('y', 1000) }).Length.Should().Be(SmsChannel.MaxLength);

        var requests = new List<CapturedRequest>();
        var sender = new ClickatellSmsSender(Factory(_ => Json(HttpStatusCode.Accepted, "{\"messages\":[{\"accepted\":true}]}"), requests),
            Microsoft.Extensions.Options.Options.Create(Options(o => { o.Sms.Provider = "Clickatell"; o.Sms.ApiKey = "key"; o.Sms.From = "TenderSync"; })), NullLogger<ClickatellSmsSender>.Instance);
        sender.IsConfigured.Should().BeTrue();

        var result = await new SmsChannel(sender).SendAsync(Message with { Recipient = "+27821234567" });

        result.Success.Should().BeTrue();
        requests.Single().Authorization.Should().Be("key");
        requests.Single().Url.Should().Be("https://platform.clickatell.com/messages");
        using var payload = JsonDocument.Parse(requests.Single().Body);
        var first = payload.RootElement.GetProperty("messages")[0];
        first.GetProperty("to").GetString().Should().Be("27821234567");
        first.GetProperty("channel").GetString().Should().Be("sms");
        first.GetProperty("content").GetString().Should().StartWith(Message.Subject);
    }

    [Fact]
    public void The_registration_provides_one_channel_per_enum_value()
    {
        var services = new ServiceCollection().AddLogging().AddNotificationChannels(null).BuildServiceProvider();

        var channels = services.GetServices<INotificationChannel>().Select(c => c.Channel).ToList();

        channels.Should().BeEquivalentTo(Enum.GetValues<Domain.Enums.NotificationChannel>());
        services.GetRequiredService<ISmsSender>().Should().BeOfType<NoOpSmsSender>();
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private sealed record CapturedRequest(string Url, string? Authorization, string Body);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond, List<CapturedRequest> requests) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Copy what the test needs: the client disposes the request (and its content) after sending.
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            requests.Add(new CapturedRequest(request.RequestUri!.ToString(), request.Headers.Authorization?.Scheme, body));
            return respond(request);
        }
    }
}
