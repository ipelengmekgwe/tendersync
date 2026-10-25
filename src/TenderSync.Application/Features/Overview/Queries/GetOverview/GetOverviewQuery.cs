using MediatR;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Features.Llm;
using TenderSync.Application.Features.Matches;
using TenderSync.Application.Features.Matches.Queries.ListMatches;
using TenderSync.Application.Features.Status;
using TenderSync.Application.Security;
using TenderSync.Domain.Enums;

namespace TenderSync.Application.Features.Overview.Queries.GetOverview;

public sealed record JobStatusDto(string JobName, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, bool? Success, string? Error);

public sealed record SourceStatusDto(string Source, DateTimeOffset CheckedAt, bool Ok, int? HttpStatus, int? LatencyMs);

/// <summary>Pipeline tile: the newest run of every job, failures in the last 24 hours and the newest probe per source.</summary>
public sealed record PipelineStatusDto(bool Healthy, int FailedRunsLast24Hours, IReadOnlyList<JobStatusDto> Jobs, IReadOnlyList<SourceStatusDto> Sources);

/// <summary>Model spend for the current South African calendar month. Admin only; null for other roles.</summary>
public sealed record CostSummaryDto(DateTimeOffset MonthStart, int Calls, decimal CostUsd, decimal SpentTodayUsd, decimal DailyCapUsd);

public enum ExpiringItemKind
{
    Document = 0,
    CsdRegistration = 1,
    TaxPin = 2,
    BbbeeCertificate = 3,
}

/// <summary>A company document or registration that has expired or expires within the reminder window.</summary>
public sealed record ExpiringItemDto(ExpiringItemKind Kind, Guid CompanyProfileId, string CompanyName, Guid? DocumentId, string Name, DateTimeOffset ExpiresAt, bool Expired);

/// <summary>Everything the overview page (PLAN.md §11 "/") shows, in one call.</summary>
public sealed record OverviewDto(
    DateTimeOffset GeneratedAt,
    int NewMatchCount,
    IReadOnlyList<MatchListItemDto> NewMatches,
    int ClosingSoonDays,
    IReadOnlyList<MatchListItemDto> ClosingSoon,
    PipelineStatusDto Pipeline,
    CostSummaryDto? Cost,
    int ExpiryWindowDays,
    IReadOnlyList<ExpiringItemDto> Expiring);

/// <summary>GET /overview: new matches, matched tenders closing soon, pipeline health, model cost this month (Admin) and expiring company documents.</summary>
[Authorize(Roles.Viewer)]
public sealed record GetOverviewQuery : IQuery<OverviewDto>;

public sealed class GetOverviewQueryHandler(
    ISender sender,
    IReadDb db,
    ICurrentUser user,
    IDateTimeProvider clock,
    LlmCostCap cap,
    IOptions<LlmOptions> llmOptions,
    IOptions<StatusOptions> statusOptions) : IRequestHandler<GetOverviewQuery, OverviewDto>
{
    private const int ListSize = 5;
    private const int ClosingSoonListSize = 10;

    private const string JobsSql = """
        select distinct on (job_name) job_name, started_at, finished_at, success, error
        from tendersync.job_runs
        order by job_name, started_at desc
        """;

    private const string FailedSql = """
        select count(*)::int from tendersync.job_runs where success = false and started_at >= @since
        """;

    private const string SourcesSql = """
        select distinct on (source) source, checked_at, ok, http_status, latency_ms
        from tendersync.source_health
        order by source, checked_at desc
        """;

    private const string CostSql = """
        select count(*)::int as calls, coalesce(sum(cost_usd), 0) as cost_usd
        from tendersync.llm_calls
        where at >= @from
        """;

    private const string ExpiringSql = """
        select 0 as kind, p.id as company_profile_id, p.name as company_name, d.id as document_id, d.title as name, d.expires_at
        from tendersync.company_documents d
        join tendersync.company_profiles p on p.id = d.company_profile_id
        where p.is_active and d.deleted_at is null and d.expires_at is not null and d.expires_at <= @until
        union all
        select 1, p.id, p.name, null, 'CSD registration', p.csd_expiry from tendersync.company_profiles p where p.is_active and p.csd_expiry <= @until
        union all
        select 2, p.id, p.name, null, 'Tax compliance PIN', p.tax_pin_expiry from tendersync.company_profiles p where p.is_active and p.tax_pin_expiry <= @until
        union all
        select 3, p.id, p.name, null, 'B-BBEE certificate', p.bbbee_expiry from tendersync.company_profiles p where p.is_active and p.bbbee_expiry <= @until
        order by expires_at
        limit 20
        """;

    public async Task<OverviewDto> Handle(GetOverviewQuery request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var closingSoonDays = statusOptions.Value.ClosingSoonDays;
        var expiryDays = statusOptions.Value.DocumentExpiryDays;

        var newMatches = await sender.Send(new ListMatchesQuery(Status: MatchStatus.New, Sort: MatchSort.NewestFirst, PageSize: ListSize), cancellationToken);
        var closingSoon = await sender.Send(new ListMatchesQuery(ClosingWithinDays: closingSoonDays, ActiveOnly: true, PageSize: ClosingSoonListSize), cancellationToken);

        var jobs = await db.QueryAsync<JobRow>(JobsSql, null, cancellationToken);
        var failed = await db.ExecuteScalarAsync<int>(FailedSql, new { since = now.AddHours(-24) }, cancellationToken);
        var sources = await db.QueryAsync<SourceRow>(SourcesSql, null, cancellationToken);
        var pipeline = new PipelineStatusDto(
            failed == 0 && sources.All(s => s.Ok),
            failed,
            jobs.Select(j => new JobStatusDto(j.JobName, j.StartedAt, j.FinishedAt, j.Success, j.Error)).ToList(),
            sources.Select(s => new SourceStatusDto(s.Source, s.CheckedAt, s.Ok, s.HttpStatus, s.LatencyMs)).ToList());

        CostSummaryDto? cost = null;
        if (user.Roles.Contains(Roles.Admin))
        {
            var monthStart = SastMonthStart(now);
            var row = (await db.QueryAsync<CostRow>(CostSql, new { from = monthStart }, cancellationToken)).Single();
            cost = new CostSummaryDto(monthStart, row.Calls, row.CostUsd, await cap.SpentTodayAsync(cancellationToken), llmOptions.Value.DailyCostCapUsd);
        }

        var expiring = await db.QueryAsync<ExpiringRow>(ExpiringSql, new { until = now.AddDays(expiryDays) }, cancellationToken);

        return new OverviewDto(
            now,
            newMatches.TotalCount,
            newMatches.Items,
            closingSoonDays,
            closingSoon.Items,
            pipeline,
            cost,
            expiryDays,
            expiring.Select(e => new ExpiringItemDto((ExpiringItemKind)e.Kind, e.CompanyProfileId, e.CompanyName, e.DocumentId, e.Name, e.ExpiresAt, e.ExpiresAt <= now)).ToList());
    }

    /// <summary>Midnight on the first of the month in South Africa (UTC+2, no daylight saving), as UTC.</summary>
    public static DateTimeOffset SastMonthStart(DateTimeOffset now)
    {
        var sast = now.ToOffset(TimeSpan.FromHours(2));
        return new DateTimeOffset(sast.Year, sast.Month, 1, 0, 0, 0, TimeSpan.FromHours(2)).ToUniversalTime();
    }

    private sealed class JobRow
    {
        public string JobName { get; init; } = string.Empty;

        public DateTimeOffset StartedAt { get; init; }

        public DateTimeOffset? FinishedAt { get; init; }

        public bool? Success { get; init; }

        public string? Error { get; init; }
    }

    private sealed class SourceRow
    {
        public string Source { get; init; } = string.Empty;

        public DateTimeOffset CheckedAt { get; init; }

        public bool Ok { get; init; }

        public int? HttpStatus { get; init; }

        public int? LatencyMs { get; init; }
    }

    private sealed class CostRow
    {
        public int Calls { get; init; }

        public decimal CostUsd { get; init; }
    }

    private sealed class ExpiringRow
    {
        public int Kind { get; init; }

        public Guid CompanyProfileId { get; init; }

        public string CompanyName { get; init; } = string.Empty;

        public Guid? DocumentId { get; init; }

        public string Name { get; init; } = string.Empty;

        public DateTimeOffset ExpiresAt { get; init; }
    }
}
