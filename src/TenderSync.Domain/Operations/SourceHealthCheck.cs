using TenderSync.Domain.Common;

namespace TenderSync.Domain.Operations;

/// <summary>Result of probing an external source (API reachability, download endpoint, schema drift).</summary>
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
        return new SourceHealthCheck
        {
            Source = Guard.NotEmpty(source, nameof(source)),
            Ok = ok,
            HttpStatus = httpStatus,
            LatencyMs = latencyMs,
            SchemaDriftJson = Guard.Trimmed(schemaDriftJson),
            Error = Guard.Trimmed(error),
            CheckedAt = at,
        };
    }
}
