using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Notifications;

/// <summary>One message to one recipient on one channel. The dedupe key prevents duplicates (PLAN.md §7.6).</summary>
public sealed class Notification : Entity
{
    public const int MaxAttempts = 5;

    private Notification()
    {
    }

    public string DedupeKey { get; private set; } = null!;

    public NotificationType Type { get; private set; }

    public NotificationChannel Channel { get; private set; }

    public string Recipient { get; private set; } = null!;

    public Guid? TenderId { get; private set; }

    public Guid? CompanyProfileId { get; private set; }

    public string Subject { get; private set; } = null!;

    public string Body { get; private set; } = null!;

    public NotificationStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? LastAttemptAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public string? Error { get; private set; }

    public string? PayloadJson { get; private set; }

    /// <summary>Earliest send time: set by quiet hours or by the retry backoff; null means send now.</summary>
    public DateTimeOffset? NotBefore { get; private set; }

    public bool IsDue(DateTimeOffset now) => Status == NotificationStatus.Pending && (NotBefore is null || NotBefore <= now);

    public static string BuildDedupeKey(NotificationType type, NotificationChannel channel, string recipient, Guid? tenderId, Guid? companyProfileId, string? bucket = null) =>
        string.Join(':', type.ToString().ToLowerInvariant(), channel.ToString().ToLowerInvariant(), recipient.Trim().ToLowerInvariant(), tenderId?.ToString("N") ?? "-", companyProfileId?.ToString("N") ?? "-", bucket ?? "-");

    public static Notification Create(NotificationType type, NotificationChannel channel, string recipient, string subject, string body, Guid? tenderId, Guid? companyProfileId, string? bucket = null, string? payloadJson = null, DateTimeOffset? notBefore = null)
    {
        recipient = Guard.NotEmpty(recipient, nameof(recipient));
        return new Notification
        {
            DedupeKey = BuildDedupeKey(type, channel, recipient, tenderId, companyProfileId, bucket),
            Type = type,
            Channel = channel,
            Recipient = recipient,
            Subject = Guard.NotEmpty(subject, nameof(subject)),
            Body = Guard.NotEmpty(body, nameof(body)),
            TenderId = tenderId,
            CompanyProfileId = companyProfileId,
            Status = NotificationStatus.Pending,
            PayloadJson = payloadJson,
            NotBefore = notBefore,
        };
    }

    /// <summary>Retry delay after a failed attempt: 1, 2, 4, 8 minutes (PLAN.md §7.6: five attempts with backoff).</summary>
    public static TimeSpan RetryDelay(int attempts) => TimeSpan.FromMinutes(Math.Pow(2, Math.Clamp(attempts - 1, 0, 6)));

    public void Defer(DateTimeOffset until) => NotBefore = until;

    public void MarkSent(DateTimeOffset now)
    {
        Attempts++;
        LastAttemptAt = now;
        SentAt = now;
        Status = NotificationStatus.Sent;
        Error = null;
        NotBefore = null;
    }

    /// <summary>Records a failed attempt and schedules the retry with backoff. Returns true when the notification will be retried.</summary>
    public bool MarkFailed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastAttemptAt = now;
        Error = Guard.NotEmpty(error, nameof(error));
        Status = Attempts >= MaxAttempts ? NotificationStatus.Failed : NotificationStatus.Pending;
        NotBefore = Status == NotificationStatus.Pending ? now + RetryDelay(Attempts) : null;
        return Status == NotificationStatus.Pending;
    }

    public void Skip(string reason)
    {
        Status = NotificationStatus.Skipped;
        Error = Guard.NotEmpty(reason, nameof(reason));
    }
}
