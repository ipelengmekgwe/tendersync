using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Features.Requirements.Golden;
using TenderSync.Infrastructure;
using Xunit.Abstractions;

namespace TenderSync.Evals;

/// <summary>
/// Field-accuracy runner for PLAN.md §7.4 over docs/golden/extraction/*.json. Compares the labels against the
/// requirements stored in the database for each tender (run `--extract ocid,... --extraction-template docs/golden/extraction`
/// first), so it costs nothing to re-run. Needs TENDERSYNC_EVALS=1 and TENDERSYNC_CONNECTION; target accuracy ≥ 0.80.
/// </summary>
public class ExtractionEvalTests(ITestOutputHelper output)
{
    private static readonly string GoldenDir = Path.Combine(AppContext.BaseDirectory, "golden", "extraction");

    [Fact]
    public async Task Field_accuracy_on_the_labelled_extraction_set()
    {
        var connection = Environment.GetEnvironmentVariable("TENDERSYNC_CONNECTION");
        var files = Directory.Exists(GoldenDir) ? Directory.GetFiles(GoldenDir, "*.json") : [];
        var labelled = files.Select(f => System.Text.Json.JsonSerializer.Deserialize<ExtractionGoldenFile>(File.ReadAllText(f), ExtractionAccuracy.Json)!)
            .Where(g => g.Labels != ExtractionLabels.Empty).ToList();
        if (Environment.GetEnvironmentVariable("TENDERSYNC_EVALS") != "1" || string.IsNullOrWhiteSpace(connection) || labelled.Count == 0)
        {
            output.WriteLine($"Extraction eval skipped: needs TENDERSYNC_EVALS=1, TENDERSYNC_CONNECTION and labelled files in {GoldenDir} ({files.Length} file(s), {labelled.Count} labelled).");
            return;
        }

        await using var provider = new ServiceCollection().AddLogging().AddInfrastructure(connection).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var scores = new List<double>();
        foreach (var golden in labelled)
        {
            var ids = await db.Tenders.GetIdsByOcidsAsync([golden.Ocid]);
            if (!ids.TryGetValue(golden.Ocid, out var id))
            {
                output.WriteLine($"  {golden.Ocid}: not in the database, skipped");
                continue;
            }

            var tender = (await db.Tenders.GetWithDetailsAsync(id))!;
            var rows = await db.Tenders.GetRequirementsAsync(id);
            var score = ExtractionAccuracy.Score(golden.Labels, ExtractionAccuracy.FromStored(tender, rows));
            scores.Add(score.Accuracy);
            output.WriteLine($"  {golden.Ocid} {golden.BidNumber}: {score.Accuracy:P0} (closing {score.ClosingAt}, briefing {score.BriefingAt}/{score.BriefingCompulsory}, threshold {score.FunctionalityThreshold}, method {score.EvaluationMethod}, mandatory F1 {score.MandatoryTypesF1:F2}, forms F1 {score.RequiredFormCodesF1:F2})");
        }

        scores.Should().NotBeEmpty();
        var average = scores.Average();
        output.WriteLine($"Field accuracy over {scores.Count} tender(s): {average:P1}");
        average.Should().BeGreaterThanOrEqualTo(0.80);
    }
}
