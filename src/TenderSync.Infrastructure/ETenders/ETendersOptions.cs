using System.ComponentModel.DataAnnotations;

namespace TenderSync.Infrastructure.ETenders;

/// <summary>eTenders OCDS API settings. PageSize stays at 100: 200 returns HTTP 500 and 1000 times out (PLAN.md §2).</summary>
public sealed class ETendersOptions
{
    public const string SectionName = "ETenders";

    [Required]
    public string BaseUrl { get; set; } = "https://ocds-api.etenders.gov.za/";

    [Required]
    public string TenderDetailsUrlFormat { get; set; } = "https://www.etenders.gov.za/Home/TenderDetails?Id={0}";

    [Range(1, 100)]
    public int PageSize { get; set; } = 100;

    [Range(1, 10000)]
    public int MaxPagesPerRun { get; set; } = 200;

    [Range(1, 600)]
    public int AttemptTimeoutSeconds { get; set; } = 60;

    [Range(1, 3600)]
    public int TotalTimeoutSeconds { get; set; } = 300;

    /// <summary>Retries after the first attempt. The resilience library requires at least one.</summary>
    [Range(1, 10)]
    public int RetryCount { get; set; } = 5;

    [Range(0, 60)]
    public double RetryBaseDelaySeconds { get; set; } = 2;

    [Range(0.01, 1)]
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;

    [Range(1, 100)]
    public int CircuitBreakerMinimumThroughput { get; set; } = 5;

    [Range(1, 3600)]
    public int CircuitBreakerSamplingSeconds { get; set; } = 120;

    [Range(1, 3600)]
    public int CircuitBreakerBreakSeconds { get; set; } = 60;

    /// <summary>Rolling window for the recurring ingest (PLAN.md §7.1).</summary>
    [Range(1, 30)]
    public int RecentWindowDays { get; set; } = 3;

    [Range(1, 730)]
    public int BackfillDays { get; set; } = 90;

    [Range(1, 30)]
    public int BackfillSliceDays { get; set; } = 7;
}
