using TenderSync.Application.Features.Classification.Golden;
using TenderSync.Domain.Enums;

namespace TenderSync.Application.Tests.Classification;

public class GoldenSetTests
{
    [Fact]
    public void Metrics_count_the_confusion_matrix()
    {
        var metrics = EvalMetrics.From([(true, true), (true, true), (true, false), (false, true), (false, false), (false, false)]);

        metrics.Should().Be(new EvalMetrics(2, 1, 1, 2));
        metrics.Precision.Should().BeApproximately(0.667, 0.001, "2 of 3 positives were right");
        metrics.Recall.Should().BeApproximately(0.667, 0.001, "2 of 3 actual matches were found");
        metrics.F1.Should().BeApproximately(0.667, 0.001);
        metrics.Total.Should().Be(6);
        EvalMetrics.From([]).Precision.Should().Be(0);
    }

    [Fact]
    public void The_jsonl_file_round_trips_rows_and_keeps_null_labels()
    {
        var path = Path.Combine(Path.GetTempPath(), "tendersync-golden", Guid.NewGuid().ToString("N"), "classification.jsonl");
        var rows = new[]
        {
            new GoldenRow("ocds-1", "B1", "Custom software", "desc", "Buyer", ProcurementCategory.Services, "RFQ", ["a.pdf"], true, ["SW_DEV"], "Keyword hits: SW_DEV=3", true, ["SW_DEV"], "clear IT"),
            new GoldenRow("ocds-2", "B2", "UPS replacement", "desc", "Buyer", ProcurementCategory.Goods, null, [], false, [], "Not IT", null, null, null),
        };
        try
        {
            GoldenSetFile.Write(path, rows);
            var lines = File.ReadAllLines(path);
            lines.Should().HaveCount(2);
            lines[0].Should().Contain("\"isItMatch\":true").And.Contain("\"mainCategory\":\"Services\"");
            lines[1].Should().Contain("\"isItMatch\":null");

            var read = GoldenSetFile.Read(path);
            read.Should().BeEquivalentTo(rows);
            read[0].ToText().Title.Should().Be("Custom software");
            GoldenSetFile.Read(path + ".missing").Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
