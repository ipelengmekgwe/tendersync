namespace TenderSync.Application.Abstractions.Persistence;

/// <summary>Read-side SQL access (Dapper) for list pages, dashboards and full-text search. Never used for writes.</summary>
public interface IReadDb
{
    Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default);

    Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default);

    Task<T> ExecuteScalarAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default);
}
