using FluentValidation;
using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;

namespace TenderSync.Application.Features.Admin.Queries.ListSourceHealth;

public sealed record SourceHealthDto(Guid Id, string Source, DateTimeOffset CheckedAt, bool Ok, int? HttpStatus, int? LatencyMs, string? SchemaDriftJson, string? Error);

/// <summary>Latest health probes per source, newest first.</summary>
[Authorize(Roles.Admin)]
public sealed record ListSourceHealthQuery(string? Source = null, int Take = 50) : IQuery<IReadOnlyList<SourceHealthDto>>;

public sealed class ListSourceHealthQueryValidator : AbstractValidator<ListSourceHealthQuery>
{
    public ListSourceHealthQueryValidator()
    {
        RuleFor(q => q.Take).InclusiveBetween(1, 500);
    }
}

public sealed class ListSourceHealthQueryHandler(IReadDb db) : IRequestHandler<ListSourceHealthQuery, IReadOnlyList<SourceHealthDto>>
{
    private const string Sql = """
        select id, source, checked_at, ok, http_status, latency_ms, schema_drift_json::text as schema_drift_json, error
        from tendersync.source_health
        where (@source::text is null or source = @source)
        order by checked_at desc
        limit @take
        """;

    public async Task<IReadOnlyList<SourceHealthDto>> Handle(ListSourceHealthQuery request, CancellationToken cancellationToken)
    {
        var rows = await db.QueryAsync<Row>(Sql, new { source = request.Source, take = request.Take }, cancellationToken);
        return rows.Select(r => new SourceHealthDto(r.Id, r.Source, r.CheckedAt, r.Ok, r.HttpStatus, r.LatencyMs, r.SchemaDriftJson, r.Error)).ToList();
    }

    private sealed class Row
    {
        public Guid Id { get; init; }

        public string Source { get; init; } = string.Empty;

        public DateTimeOffset CheckedAt { get; init; }

        public bool Ok { get; init; }

        public int? HttpStatus { get; init; }

        public int? LatencyMs { get; init; }

        public string? SchemaDriftJson { get; init; }

        public string? Error { get; init; }
    }
}
