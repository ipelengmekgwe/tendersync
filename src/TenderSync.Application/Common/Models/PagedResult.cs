namespace TenderSync.Application.Common.Models;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class PagedResult
{
    public static PagedResult<T> Empty<T>(int page, int pageSize) => new([], page, pageSize, 0);
}

/// <summary>Shared paging bounds for list queries.</summary>
public static class Paging
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 200;

    public static int Offset(int page, int pageSize) => (Math.Max(page, 1) - 1) * pageSize;
}
