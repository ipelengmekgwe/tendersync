using TenderSync.Domain.Notifications;

namespace TenderSync.Application.Abstractions.Persistence;

public interface INotificationRepository : IRepository<Notification>
{
    Task<bool> ExistsAsync(string dedupeKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Notification>> GetPendingAsync(int batchSize, CancellationToken cancellationToken = default);

    /// <summary>Pending rows whose NotBefore has passed (or is null) and that were created before <paramref name="createdBefore"/>, oldest first.</summary>
    Task<IReadOnlyList<Notification>> GetDueAsync(DateTimeOffset now, DateTimeOffset createdBefore, int batchSize, CancellationToken cancellationToken = default);
}
