using TenderSync.Domain.Common;
using TenderSync.Domain.Events;

namespace TenderSync.Domain.Operations;

/// <summary>Result of probing an external source (API reachability, download endpoint, schema drift). Raises <see cref="PipelineFailure"/> when unhealthy.</summary>
public sealed class SourceHealthCheck : Entity
{
    private SourceHealthCheck()
    {
    }

    public string Source { get; private set; } = null!;

    public DateTimeOffset CheckedAt { get; private set; }

    public bool Ok { get; private set; }

    public int? HttpStatus { get; private set; }

    public int? LatencyMs { get; private set; }

    public string? SchemaDriftJson { get; private set; }

    public string? Error { get; private set; }

    public static SourceHealthCheck Record(string source, bool ok, int? httpStatus, int? latencyMs, string? schemaDriftJson, string? error, DateTimeOffset at)
    {
        var check = new SourceHealthCheck
        {
            Source = Guard.NotEmpty(source, nameof(source)),
            Ok = ok,
            HttpStatus = httpStatus,
            LatencyMs = latencyMs,
            SchemaDriftJson = Guard.Trimmed(schemaDriftJson),
            Error = Guard.Trimmed(error),
            CheckedAt = at,
        };

        if (!ok)
        {
            check.Raise(new PipelineFailure(check.Source, check.Error ?? "Source unhealthy", check.SchemaDriftJson, at));
        }
        else if (check.SchemaDriftJson is not null)
        {
            check.Raise(new PipelineFailure(check.Source, "Source schema drift detected", check.SchemaDriftJson, at));
        }

        return check;
    }
}
