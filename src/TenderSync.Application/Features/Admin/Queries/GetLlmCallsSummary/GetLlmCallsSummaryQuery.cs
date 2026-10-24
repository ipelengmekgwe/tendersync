using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Features.Llm;
using TenderSync.Application.Security;

namespace TenderSync.Application.Features.Admin.Queries.GetLlmCallsSummary;

public sealed record LlmCallsBucketDto(string Key, int Calls, int Failed, long TokensIn, long TokensOut, long CacheRead, decimal CostUsd);

public sealed record LlmCallDto(Guid Id, DateTimeOffset At, string Purpose, string Provider, string Model, string? PromptVersion, Guid? TenderId, int TokensIn, int TokensOut, int CacheReadTokens, decimal CostUsd, int DurationMs, bool Success, string? Error);

public sealed record LlmCallsSummaryDto(
    DateTimeOffset From,
    DateTimeOffset To,
    int Calls,
    int Failed,
    long TokensIn,
    long TokensOut,
    long CacheRead,
    decimal CostUsd,
    decimal SpentTodayUsd,
    decimal DailyCapUsd,
    IReadOnlyList<LlmCallsBucketDto> ByDay,
    IReadOnlyList<LlmCallsBucketDto> ByPurpose,
    IReadOnlyList<LlmCallsBucketDto> ByModel,
    IReadOnlyList<LlmCallDto> Recent);

/// <summary>GET /admin/llm-calls: cost summary (PLAN.md §10) for a window, default the last 30 days, plus the latest calls.</summary>
[Authorize(Roles.Admin)]
public sealed record GetLlmCallsSummaryQuery(DateTimeOffset? From = null, DateTimeOffset? To = null, string? Purpose = null, int Recent = 50) : IQuery<LlmCallsSummaryDto>;

public sealed class GetLlmCallsSummaryQueryValidator : AbstractValidator<GetLlmCallsSummaryQuery>
{
    public GetLlmCallsSummaryQueryValidator()
    {
        RuleFor(q => q.Recent).InclusiveBetween(0, 500);
        RuleFor(q => q).Must(q => q.From is null || q.To is null || q.From <= q.To).WithMessage("From must be before To.");
    }
}

public sealed class GetLlmCallsSummaryQueryHandler(IReadDb db, LlmCostCap cap, IOptions<LlmOptions> options, IDateTimeProvider clock) : IRequestHandler<GetLlmCallsSummaryQuery, LlmCallsSummaryDto>
{
    private const string BucketSql = """
        select {key} as key, count(*)::int as calls, count(*) filter (where not success)::int as failed,
               coalesce(sum(tokens_in), 0)::bigint as tokens_in, coalesce(sum(tokens_out), 0)::bigint as tokens_out,
               coalesce(sum(cache_read_tokens), 0)::bigint as cache_read, coalesce(sum(cost_usd), 0) as cost_usd
        from tendersync.llm_calls
        where at >= @from and at < @to and (@purpose::text is null or purpose = @purpose)
        group by {key}
        order by {order}
        """;

    private const string RecentSql = """
        select id, at, purpose, provider, model, prompt_version, tender_id, tokens_in, tokens_out, cache_read_tokens, cost_usd, duration_ms, success, error
        from tendersync.llm_calls
        where at >= @from and at < @to and (@purpose::text is null or purpose = @purpose)
        order by at desc
        limit @recent
        """;

    public async Task<LlmCallsSummaryDto> Handle(GetLlmCallsSummaryQuery request, CancellationToken cancellationToken)
    {
        var to = request.To ?? clock.UtcNow.AddMinutes(1);
        var from = request.From ?? to.AddDays(-30);
        var args = new { from, to, purpose = request.Purpose, recent = request.Recent };

        var byDay = await db.QueryAsync<BucketRow>(Bucket("to_char((at at time zone 'Africa/Johannesburg')::date, 'YYYY-MM-DD')", "key desc"), args, cancellationToken);
        var byPurpose = await db.QueryAsync<BucketRow>(Bucket("purpose", "cost_usd desc"), args, cancellationToken);
        var byModel = await db.QueryAsync<BucketRow>(Bucket("provider || '/' || model", "cost_usd desc"), args, cancellationToken);
        var recent = request.Recent == 0 ? [] : await db.QueryAsync<CallRow>(RecentSql, args, cancellationToken);

        return new LlmCallsSummaryDto(
            from, to,
            byPurpose.Sum(b => b.Calls), byPurpose.Sum(b => b.Failed), byPurpose.Sum(b => b.TokensIn), byPurpose.Sum(b => b.TokensOut), byPurpose.Sum(b => b.CacheRead), byPurpose.Sum(b => b.CostUsd),
            await cap.SpentTodayAsync(cancellationToken),
            options.Value.DailyCostCapUsd,
            byDay.Select(Map).ToList(),
            byPurpose.Select(Map).ToList(),
            byModel.Select(Map).ToList(),
            recent.Select(r => new LlmCallDto(r.Id, r.At, r.Purpose, r.Provider, r.Model, r.PromptVersion, r.TenderId, r.TokensIn, r.TokensOut, r.CacheReadTokens, r.CostUsd, r.DurationMs, r.Success, r.Error)).ToList());
    }

    /// <summary>The key expressions are constants from this file, never user input.</summary>
    private static string Bucket(string keyExpression, string order) => BucketSql.Replace("{key}", keyExpression, StringComparison.Ordinal).Replace("{order}", order, StringComparison.Ordinal);

    private static LlmCallsBucketDto Map(BucketRow r) => new(r.Key, r.Calls, r.Failed, r.TokensIn, r.TokensOut, r.CacheRead, r.CostUsd);

    private sealed class BucketRow
    {
        public string Key { get; init; } = string.Empty;

        public int Calls { get; init; }

        public int Failed { get; init; }

        public long TokensIn { get; init; }

        public long TokensOut { get; init; }

        public long CacheRead { get; init; }

        public decimal CostUsd { get; init; }
    }

    private sealed class CallRow
    {
        public Guid Id { get; init; }

        public DateTimeOffset At { get; init; }

        public string Purpose { get; init; } = string.Empty;

        public string Provider { get; init; } = string.Empty;

        public string Model { get; init; } = string.Empty;

        public string? PromptVersion { get; init; }

        public Guid? TenderId { get; init; }

        public int TokensIn { get; init; }

        public int TokensOut { get; init; }

        public int CacheReadTokens { get; init; }

        public decimal CostUsd { get; init; }

        public int DurationMs { get; init; }

        public bool Success { get; init; }

        public string? Error { get; init; }
    }
}
