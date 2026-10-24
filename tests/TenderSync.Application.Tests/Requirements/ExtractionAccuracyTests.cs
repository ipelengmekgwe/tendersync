using TenderSync.Application.Features.Requirements.Golden;
using TenderSync.Domain.Enums;

namespace TenderSync.Application.Tests.Requirements;

public class ExtractionAccuracyTests
{
    private static readonly DateTimeOffset Closing = new(2026, 11, 20, 11, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Scores_each_key_field_and_averages_the_defined_ones()
    {
        var labels = new ExtractionLabels(Closing, new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.FromHours(2)), true, 70, "80/20", [RequirementType.CsdRegistration, RequirementType.TaxCompliance, RequirementType.BbbeeCertificate], ["SBD1", "SBD4", "SBD6.1"]);
        var extracted = new ExtractionLabels(Closing.ToUniversalTime(), null, true, 70, "80 / 20", [RequirementType.CsdRegistration, RequirementType.TaxCompliance, RequirementType.References], ["sbd 1", "SBD 6.1"]);

        var scores = ExtractionAccuracy.Score(labels, extracted);

        scores.ClosingAt.Should().BeTrue("same instant in a different offset");
        scores.BriefingAt.Should().BeFalse();
        scores.BriefingCompulsory.Should().BeTrue();
        scores.FunctionalityThreshold.Should().BeTrue();
        scores.EvaluationMethod.Should().BeTrue("spacing and punctuation are ignored");
        scores.MandatoryTypesF1.Should().BeApproximately(0.667, 0.001, "2 of 3 expected found, 1 extra");
        scores.RequiredFormCodesF1.Should().BeApproximately(0.8, 0.001, "2 of 3 expected found, none extra");
        scores.Accuracy.Should().BeApproximately((1 + 0 + 1 + 1 + 1 + 0.667 + 0.8) / 7, 0.001);
    }

    [Fact]
    public void Undefined_label_fields_are_skipped_and_empty_sets_count_as_correct()
    {
        var labels = ExtractionLabels.Empty;
        var extracted = new ExtractionLabels(Closing, null, null, 60, null, [], []);

        var scores = ExtractionAccuracy.Score(labels, extracted);

        scores.ClosingAt.Should().BeNull();
        scores.FunctionalityThreshold.Should().BeNull("the label says 'not in the bid pack' with null, so it is not scored");
        scores.MandatoryTypesF1.Should().Be(1);
        scores.Accuracy.Should().Be(1);
        ExtractionAccuracy.SetF1(["a"], []).Should().Be(0);
    }

    [Fact]
    public void Golden_files_round_trip_with_enum_names()
    {
        var file = new ExtractionGoldenFile("ocds-1", "B1", "Title", new ExtractionLabels(Closing, null, true, 70, "80/20", [RequirementType.CsdRegistration], ["SBD6.1"]), null, "note");

        var json = System.Text.Json.JsonSerializer.Serialize(file, ExtractionAccuracy.Json);
        json.Should().Contain("\"CsdRegistration\"").And.Contain("\"labels\"");
        System.Text.Json.JsonSerializer.Deserialize<ExtractionGoldenFile>(json, ExtractionAccuracy.Json).Should().BeEquivalentTo(file);
    }
}
