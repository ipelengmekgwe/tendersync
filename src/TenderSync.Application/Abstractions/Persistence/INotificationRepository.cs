using TenderSync.Domain.Notifications;

namespace TenderSync.Application.Abstractions.Persistence;

public interface INotificationRepository : IRepository<Notification>
{
    Task<bool> ExistsAsync(string dedupeKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Notification>> GetPendingAsync(int batchSize, CancellationToken cancellationToken = default);
}
