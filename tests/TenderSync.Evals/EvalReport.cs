using Xunit.Abstractions;

namespace TenderSync.Evals;

/// <summary>
/// Writes eval results to the test output and, on GitHub Actions, to the job summary ($GITHUB_STEP_SUMMARY), so the
/// nightly run shows its numbers on the run page without opening logs.
/// </summary>
public static class EvalReport
{
    private static readonly Lock Gate = new();

    public static void Line(ITestOutputHelper output, string text)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.WriteLine(text);
        Summary(text);
    }

    /// <summary>Appends to the GitHub job summary only; a no-op outside Actions.</summary>
    public static void Summary(string text)
    {
        var summary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (string.IsNullOrEmpty(summary))
        {
            return;
        }

        lock (Gate)
        {
            File.AppendAllText(summary, text + Environment.NewLine);
        }
    }
}
