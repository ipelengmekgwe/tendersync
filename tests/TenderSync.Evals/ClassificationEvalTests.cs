using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Llm;
using TenderSync.Application.Features.Classification;
using TenderSync.Application.Features.Classification.Golden;
using TenderSync.Application.Features.Llm;
using TenderSync.Domain.Categories;
using TenderSync.Infrastructure.Llm;
using TenderSync.Infrastructure.Persistence.Seed;
using Xunit.Abstractions;

namespace TenderSync.Evals;

/// <summary>
/// The precision/recall runner for PLAN.md §7.2 over docs/golden/classification.jsonl (rows with a reviewed label).
/// Stage 1 runs always (it is a recall filter: labelled IT matches must pass it). Stage 2 calls the model and runs only
/// with TENDERSYNC_EVALS=1 and an Anthropic key. Targets: recall ≥ 0.90, precision ≥ 0.70 for "IT match".
/// </summary>
public class ClassificationEvalTests(ITestOutputHelper output)
{
    private static readonly string GoldenPath = Path.Combine(AppContext.BaseDirectory, "golden", "classification.jsonl");

    private static IReadOnlyList<InterestCategory> Categories { get; } =
        InterestCategorySeed.All.Select(r => InterestCategory.Create(r.Code, r.Name, r.Description, r.Keywords, r.NegativeKeywords)).ToList();

    private static List<GoldenRow> Labelled() => GoldenSetFile.Read(GoldenPath).Where(r => r.IsItMatch is not null).ToList();

    [Fact]
    public void Stage_one_recall_on_the_reviewed_golden_set()
    {
        var rows = Labelled();
        if (rows.Count == 0)
        {
            output.WriteLine($"No labelled rows in {GoldenPath}; review docs/golden/classification.jsonl (set isItMatch true/false) to enable this eval.");
            return;
        }

        var prefilter = new KeywordPrefilter(Options.Create(new ClassificationOptions()));
        var pairs = rows.Select(r => (prefilter.Evaluate(r.ToText(), Categories).IsCandidate, r.IsItMatch!.Value)).ToList();
        var metrics = EvalMetrics.From(pairs);
        EvalReport.Line(output, $"- Classification stage 1 (keyword prefilter): {metrics}");
        foreach (var missed in rows.Where((r, i) => !pairs[i].Item1 && pairs[i].Item2))
        {
            output.WriteLine($"  missed: {missed.Ocid} {missed.Title}");
        }

        metrics.Recall.Should().BeGreaterThanOrEqualTo(0.90, "the keyword stage must not drop IT tenders before the model sees them");
    }

    [Fact]
    public async Task Stage_two_precision_and_recall_on_the_reviewed_golden_set()
    {
        var apiKey = Environment.GetEnvironmentVariable("Llm__Anthropic__ApiKey") ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var rows = Labelled();
        if (Environment.GetEnvironmentVariable("TENDERSYNC_EVALS") != "1" || string.IsNullOrWhiteSpace(apiKey) || rows.Count == 0)
        {
            output.WriteLine("Stage 2 eval skipped: needs TENDERSYNC_EVALS=1, an Anthropic key and labelled golden rows.");
            return;
        }

        var settings = new ClassificationOptions();
        var prefilter = new KeywordPrefilter(Options.Create(settings));
        var prompt = new PromptRegistry().Get("classify");
        var pricing = new LlmPricing(Options.Create(new LlmOptions()), NullLogger<LlmPricing>.Instance);
        var client = new AnthropicLlmClient(Options.Create(new AnthropicOptions { ApiKey = apiKey }), pricing, NullLogger<AnthropicLlmClient>.Instance);

        var pairs = new List<(bool Predicted, bool Actual)>();
        decimal cost = 0m;
        var stoppedAt = (int?)null;
        foreach (var row in rows)
        {
            var text = row.ToText();
            var predicted = false;
            if (prefilter.Evaluate(text, Categories).IsCandidate)
            {
                if (!EvalBudget.CanSpend())
                {
                    stoppedAt = pairs.Count;
                    break;
                }

                var result = await client.CompleteAsync<ClassificationAnswer>(new LlmRequest(
                    "classify", prompt.VersionTag, prompt.Body, [new LlmTextPart(TenderClassifier.BuildUserText(text, Categories, settings))], prompt.Tier, LlmEffort.Low, 1024));
                predicted = result.Value.Relevance >= settings.MatchThreshold;
                cost += result.CostUsd;
                EvalBudget.Record(result.CostUsd);
                if (predicted != row.IsItMatch)
                {
                    output.WriteLine($"  {(predicted ? "false positive" : "false negative")}: {row.Ocid} relevance {result.Value.Relevance} — {row.Title}");
                }
            }
            else if (row.IsItMatch == true)
            {
                output.WriteLine($"  false negative (stage 1): {row.Ocid} — {row.Title}");
            }

            pairs.Add((predicted, row.IsItMatch!.Value));
        }

        var metrics = EvalMetrics.From(pairs);
        EvalReport.Line(output, $"- Classification stage 2 (model): {metrics}, cost ${cost:F4} of the ${EvalBudget.MaxUsd:F2} run budget");
        if (stoppedAt is { } evaluated)
        {
            // Partial results are not comparable with the targets; report them and leave the run green but visibly short.
            EvalReport.Line(output, $"- ⚠ Budget reached after {evaluated} of {rows.Count} rows ({EvalBudget.Variable}); targets not checked.");
            return;
        }

        metrics.Recall.Should().BeGreaterThanOrEqualTo(0.90);
        metrics.Precision.Should().BeGreaterThanOrEqualTo(0.70);
    }
}
