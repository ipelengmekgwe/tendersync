using TenderSync.Domain.Notifications;

namespace TenderSync.Application.Abstractions.Persistence;

public interface INotificationPreferenceRepository : IRepository<NotificationPreference>
{
    Task<IReadOnlyList<NotificationPreference>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Every enabled preference of every user: the dispatcher's recipient list.</summary>
    Task<IReadOnlyList<NotificationPreference>> ListEnabledAsync(CancellationToken cancellationToken = default);
}
