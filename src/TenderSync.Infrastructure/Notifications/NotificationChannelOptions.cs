using System.ComponentModel.DataAnnotations;

namespace TenderSync.Infrastructure.Notifications;

/// <summary>Channel credentials (PLAN.md §7.6), bound from the "Notifications" section next to the Application options. Secrets come from user-secrets or the environment.</summary>
public sealed class NotificationChannelOptions
{
    public const string SectionName = "Notifications";

    public EmailOptions Email { get; set; } = new();

    public TelegramOptions Telegram { get; set; } = new();

    public TeamsOptions Teams { get; set; } = new();

    public SmsOptions Sms { get; set; } = new();

    public WhatsAppOptions WhatsApp { get; set; } = new();

    /// <summary>MailKit SMTP. Resend, Postmark and SES all accept SMTP credentials.</summary>
    public sealed class EmailOptions
    {
        public string SmtpHost { get; set; } = string.Empty;

        [Range(1, 65535)]
        public int SmtpPort { get; set; } = 587;

        public string SmtpUser { get; set; } = string.Empty;

        public string SmtpPassword { get; set; } = string.Empty;

        public string FromAddress { get; set; } = string.Empty;

        public string FromName { get; set; } = "TenderSync";

        /// <summary>StartTls (587), SslOnConnect (465), None (local relay) or Auto.</summary>
        public string Security { get; set; } = "StartTls";

        [Range(5, 300)]
        public int TimeoutSeconds { get; set; } = 30;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(SmtpHost) && !string.IsNullOrWhiteSpace(FromAddress);
    }

    /// <summary>Bot API. The recipient is the chat id stored on the preference.</summary>
    public sealed class TelegramOptions
    {
        public string BotToken { get; set; } = string.Empty;

        public string BaseUrl { get; set; } = "https://api.telegram.org";

        /// <summary>Optional default chat id, used by operators as the address of the first preference.</summary>
        public string ChatId { get; set; } = string.Empty;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken);
    }

    /// <summary>Incoming webhooks need no credential: the recipient is the webhook URL itself.</summary>
    public sealed class TeamsOptions
    {
        /// <summary>Optional default webhook, used by operators as the address of the first preference.</summary>
        public string WebhookUrl { get; set; } = string.Empty;
    }

    public sealed class SmsOptions
    {
        public const string NoOpProvider = "NoOp";
        public const string ClickatellProvider = "Clickatell";

        [RegularExpression("^(NoOp|Clickatell)$", ErrorMessage = "Notifications:Sms:Provider must be NoOp or Clickatell.")]
        public string Provider { get; set; } = NoOpProvider;

        public string ApiKey { get; set; } = string.Empty;

        public string BaseUrl { get; set; } = "https://platform.clickatell.com";

        public string From { get; set; } = string.Empty;
    }

    public sealed class WhatsAppOptions
    {
        public const string NoOpProvider = "NoOp";

        [RegularExpression("^(NoOp)$", ErrorMessage = "Notifications:WhatsApp:Provider must be NoOp in v1.")]
        public string Provider { get; set; } = NoOpProvider;
    }
}
