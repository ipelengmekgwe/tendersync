using System.Globalization;

namespace TenderSync.Evals;

/// <summary>
/// Dollar cap for model calls made by the evals in one run (the nightly workflow sets TENDERSYNC_EVALS_MAX_USD; default
/// $2). Evals ask before every call; once the cap is reached they stop calling and report on what they evaluated.
/// Shared across test classes, which xUnit may run in parallel.
/// </summary>
public static class EvalBudget
{
    public const string Variable = "TENDERSYNC_EVALS_MAX_USD";
    public const decimal DefaultMaxUsd = 2m;

    private static readonly Lock Gate = new();
    private static decimal _spent;

    public static decimal MaxUsd { get; } = Parse(Environment.GetEnvironmentVariable(Variable));

    public static decimal SpentUsd
    {
        get
        {
            lock (Gate)
            {
                return _spent;
            }
        }
    }

    /// <summary>True while there is money left for another call.</summary>
    public static bool CanSpend()
    {
        lock (Gate)
        {
            return _spent < MaxUsd;
        }
    }

    public static void Record(decimal costUsd)
    {
        lock (Gate)
        {
            _spent += costUsd;
        }
    }

    /// <summary>A positive decimal in invariant culture, or the default when missing or invalid.</summary>
    public static decimal Parse(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : DefaultMaxUsd;
}
