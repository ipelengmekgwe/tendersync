using System.Text.RegularExpressions;
using FluentValidation;
using TenderSync.Application.Common.Auditing;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Notifications;

namespace TenderSync.Application.Features.Notifications;

/// <summary>One channel subscription of the calling user: where to send and which types.</summary>
public sealed record NotificationPreferenceInput(
    NotificationChannel Channel,
    [property: Sensitive] string Address,
    IReadOnlyList<NotificationType> Types,
    bool Enabled = true,
    QuietHoursInput? QuietHours = null);

public sealed record QuietHoursInput(string Start, string End, string? TimeZone = null)
{
    public QuietHours ToDomain() => new(Start, End, string.IsNullOrWhiteSpace(TimeZone) ? Domain.Notifications.QuietHours.DefaultTimeZone : TimeZone);

    public static QuietHoursInput? From(QuietHours? value) => value is null ? null : new(value.Start, value.End, value.TimeZone);
}

public sealed partial class NotificationPreferenceInputValidator : AbstractValidator<NotificationPreferenceInput>
{
    public NotificationPreferenceInputValidator()
    {
        RuleFor(p => p.Address).NotEmpty().MaximumLength(1024);
        RuleFor(p => p.Address).Must(a => EmailPattern().IsMatch(a)).When(p => p.Channel == NotificationChannel.Email).WithMessage("Email address is not valid.");
        RuleFor(p => p.Address).Must(a => ChatIdPattern().IsMatch(a)).When(p => p.Channel == NotificationChannel.Telegram).WithMessage("Telegram address is the numeric chat id (or @channel).");
        RuleFor(p => p.Address).Must(a => Uri.TryCreate(a, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps).When(p => p.Channel == NotificationChannel.Teams).WithMessage("Teams address is the https incoming-webhook URL.");
        RuleFor(p => p.Address).Must(a => PhonePattern().IsMatch(a)).When(p => p.Channel is NotificationChannel.Sms or NotificationChannel.WhatsApp).WithMessage("Phone numbers are E.164 (+27821234567).");
        RuleFor(p => p.Types).NotNull();
        RuleFor(p => p.QuietHours!.Start).Matches("^([01]\\d|2[0-3]):[0-5]\\d$").When(p => p.QuietHours is not null).WithMessage("Quiet hours start is HH:mm.");
        RuleFor(p => p.QuietHours!.End).Matches("^([01]\\d|2[0-3]):[0-5]\\d$").When(p => p.QuietHours is not null).WithMessage("Quiet hours end is HH:mm.");
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^(-?\d{1,20}|@[A-Za-z0-9_]{3,64})$", RegexOptions.CultureInvariant)]
    private static partial Regex ChatIdPattern();

    [GeneratedRegex(@"^\+[1-9]\d{6,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}

public sealed record NotificationPreferenceDto(Guid Id, NotificationChannel Channel, string Address, IReadOnlyList<NotificationType> Types, bool Enabled, QuietHoursInput? QuietHours, DateTimeOffset UpdatedAt)
{
    public static NotificationPreferenceDto From(NotificationPreference p) => new(p.Id, p.Channel, p.Address, p.Types, p.Enabled, QuietHoursInput.From(p.QuietHours), p.UpdatedAt);
}

/// <summary>A notification as the owner sees it. The recipient is masked; the body is the rendered message.</summary>
public sealed record NotificationDto(
    Guid Id,
    NotificationType Type,
    NotificationChannel Channel,
    string Recipient,
    string Subject,
    string Body,
    NotificationStatus Status,
    int Attempts,
    DateTimeOffset? SentAt,
    DateTimeOffset? NotBefore,
    string? Error,
    Guid? TenderId,
    DateTimeOffset CreatedAt);
