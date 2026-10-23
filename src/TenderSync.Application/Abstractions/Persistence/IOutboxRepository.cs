using TenderSync.Domain.Notifications;

namespace TenderSync.Application.Abstractions.Persistence;

/// <summary>Domain events waiting to be dispatched. Rows are written automatically when tracked entities raise events.</summary>
public interface IOutboxRepository : IRepository<OutboxMessage>
{
    Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int batchSize, CancellationToken cancellationToken = default);
}
