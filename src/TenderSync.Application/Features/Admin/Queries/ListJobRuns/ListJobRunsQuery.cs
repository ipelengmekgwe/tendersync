using FluentValidation;
using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Common.Models;
using TenderSync.Application.Security;

namespace TenderSync.Application.Features.Admin.Queries.ListJobRuns;

public sealed record JobRunDto(Guid Id, string JobName, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, bool? Success, int ItemsProcessed, string? Error, string? DetailsJson);

[Authorize(Roles.Admin)]
public sealed record ListJobRunsQuery(string? JobName = null, int Page = 1, int PageSize = Paging.DefaultPageSize) : IQuery<PagedResult<JobRunDto>>;

public sealed class ListJobRunsQueryValidator : AbstractValidator<ListJobRunsQuery>
{
    public ListJobRunsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}

public sealed class ListJobRunsQueryHandler(IReadDb db) : IRequestHandler<ListJobRunsQuery, PagedResult<JobRunDto>>
{
    private const string Sql = """
        select id, job_name, started_at, finished_at, success, items_processed, error, details_json::text as details_json, count(*) over() as total_count
        from tendersync.job_runs
        where (@jobName::text is null or job_name = @jobName)
        order by started_at desc
        limit @pageSize offset @offset
        """;

    public async Task<PagedResult<JobRunDto>> Handle(ListJobRunsQuery request, CancellationToken cancellationToken)
    {
        var rows = await db.QueryAsync<Row>(Sql, new { jobName = request.JobName, pageSize = request.PageSize, offset = Paging.Offset(request.Page, request.PageSize) }, cancellationToken);
        if (rows.Count == 0)
        {
            return PagedResult.Empty<JobRunDto>(request.Page, request.PageSize);
        }

        var items = rows.Select(r => new JobRunDto(r.Id, r.JobName, r.StartedAt, r.FinishedAt, r.Success, r.ItemsProcessed, r.Error, r.DetailsJson)).ToList();
        return new PagedResult<JobRunDto>(items, request.Page, request.PageSize, (int)rows[0].TotalCount);
    }

    private sealed class Row
    {
        public Guid Id { get; init; }

        public string JobName { get; init; } = string.Empty;

        public DateTimeOffset StartedAt { get; init; }

        public DateTimeOffset? FinishedAt { get; init; }

        public bool? Success { get; init; }

        public int ItemsProcessed { get; init; }

        public string? Error { get; init; }

        public string? DetailsJson { get; init; }

        public long TotalCount { get; init; }
    }
}
