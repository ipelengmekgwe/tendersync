using FluentValidation;
using MediatR;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Auditing;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Notifications;

namespace TenderSync.Application.Features.Notifications.Commands.UpdateNotificationPreferences;

/// <summary>PUT /notification-preferences: replaces the caller's subscriptions (one per channel); channels left out are removed.</summary>
[Authorize(Roles.Viewer)]
public sealed record UpdateNotificationPreferencesCommand(IReadOnlyList<NotificationPreferenceInput> Preferences) : ICommand<IReadOnlyList<NotificationPreferenceDto>>, IAuditedEntityReference
{
    public string AuditEntityType => nameof(NotificationPreference);

    public string? AuditEntityId => null;
}

public sealed class UpdateNotificationPreferencesCommandValidator : AbstractValidator<UpdateNotificationPreferencesCommand>
{
    public UpdateNotificationPreferencesCommandValidator()
    {
        RuleFor(c => c.Preferences).NotNull();
        RuleForEach(c => c.Preferences).SetValidator(new NotificationPreferenceInputValidator());
        RuleFor(c => c.Preferences).Must(p => p.Select(x => x.Channel).Distinct().Count() == p.Count).WithMessage("One preference per channel.");
    }
}

public sealed class UpdateNotificationPreferencesHandler(IAppDbContext db, ICurrentUser currentUser) : IRequestHandler<UpdateNotificationPreferencesCommand, IReadOnlyList<NotificationPreferenceDto>>
{
    public async Task<IReadOnlyList<NotificationPreferenceDto>> Handle(UpdateNotificationPreferencesCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var existing = await db.NotificationPreferences.ListByUserAsync(userId, cancellationToken);
        var result = new List<NotificationPreference>();

        foreach (var input in request.Preferences)
        {
            var quiet = input.QuietHours?.ToDomain().ToJson();
            var preference = existing.FirstOrDefault(p => p.Channel == input.Channel);
            if (preference is null)
            {
                preference = NotificationPreference.Create(userId, input.Channel, input.Address, input.Types, input.Enabled);
                preference.Update(input.Address, input.Types, input.Enabled, quiet);
                db.NotificationPreferences.Add(preference);
            }
            else
            {
                preference.Update(input.Address, input.Types, input.Enabled, quiet);
            }

            result.Add(preference);
        }

        foreach (var removed in existing.Where(p => request.Preferences.All(i => i.Channel != p.Channel)))
        {
            db.NotificationPreferences.Remove(removed);
        }

        return result.OrderBy(p => p.Channel).Select(NotificationPreferenceDto.From).ToList();
    }
}
