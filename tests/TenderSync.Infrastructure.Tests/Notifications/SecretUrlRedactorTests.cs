using TenderSync.Infrastructure.Notifications;

namespace TenderSync.Infrastructure.Tests.Notifications;

public class SecretUrlRedactorTests
{
    [Theory]
    [InlineData("https://api.telegram.org/bot123456:AAH-secret_token/sendMessage", "https://api.telegram.org/bot***/sendMessage")]
    [InlineData("https://contoso.webhook.office.com/webhookb2/abc@def/IncomingWebhook/123/456", "https://contoso.webhook.office.com/***")]
    [InlineData("https://prod-12.westeurope.logic.azure.com:443/workflows/abc/triggers/manual/paths/invoke?sig=xyz", "https://prod-12.westeurope.logic.azure.com/***")]
    public void Secrets_in_channel_urls_are_hidden(string url, string expected)
    {
        SecretUrlRedactor.Redact(new Uri(url)).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://ocds-api.etenders.gov.za/api/OCDSReleases?PageNumber=1&PageSize=100")]
    [InlineData("https://www.etenders.gov.za/home/Download?blobName=bot123/abc.pdf")]
    [InlineData("https://example.org/bottles/42/details")]
    public void Other_urls_are_left_alone(string url)
    {
        SecretUrlRedactor.Redact(new Uri(url)).Should().BeNull();
    }
}
