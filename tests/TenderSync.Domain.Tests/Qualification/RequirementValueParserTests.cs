using TenderSync.Domain.Qualification;

namespace TenderSync.Domain.Tests.Qualification;

public class RequirementValueParserTests
{
    [Theory]
    [InlineData("level 4", 4)]
    [InlineData("B-BBEE Level 1 to 4", 4)]
    [InlineData("level 2 or better", 2)]
    [InlineData("Level 1 – 3 contributor", 3)]
    [InlineData("4", 4)]
    [InlineData("a valid certificate", null)]
    [InlineData("level 9", null)]
    public void Bbbee_level_is_the_worst_acceptable_level(string text, int? expected) =>
        RequirementValueParser.BbbeeLevel(text).Should().Be(expected);

    [Theory]
    [InlineData("R10 million", "10000000")]
    [InlineData("R10m", "10000000")]
    [InlineData("R 2.5 mil per annum", "2500000")]
    [InlineData("R5,000,000", "5000000")]
    [InlineData("ZAR 3 000 000", "3000000")]
    [InlineData("R2,5 million", "2500000")]
    [InlineData("R500k", "500000")]
    [InlineData("turnover of R1 billion", "1000000000")]
    [InlineData("no amount here", null)]
    public void Rand_amounts_read_units_and_separators(string text, string? expected) =>
        RequirementValueParser.Rand(text).Should().Be(expected is null ? null : decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("5 years", 5)]
    [InlineData("five (5) years", 5)]
    [InlineData("a minimum of 3 years' experience", 3)]
    [InlineData("3+ years", 3)]
    [InlineData("registered", null)]
    public void Years_read_the_number_before_the_word(string text, int? expected) =>
        RequirementValueParser.Years(text).Should().Be(expected);

    [Theory]
    [InlineData("3 references", 3)]
    [InlineData("three (3) contactable references", 3)]
    [InlineData("Two reference letters", 2)]
    [InlineData("references from clients", null)]
    public void Counts_read_digits_or_number_words(string text, int? expected) =>
        RequirementValueParser.Count(text).Should().Be(expected);

    [Theory]
    [InlineData("7GB", 7, "GB")]
    [InlineData("grade 7 GB or higher", 7, "GB")]
    [InlineData("CIDB grading 5CE", 5, "CE")]
    public void Cidb_grades_split_grade_and_class(string text, int grade, string cls) =>
        RequirementValueParser.CidbGrade(text).Should().Be((grade, cls));

    [Fact]
    public void Cidb_text_without_a_class_is_null() => RequirementValueParser.CidbGrade("CIDB registered").Should().BeNull();

    [Fact]
    public void Reference_spec_reads_count_value_and_window()
    {
        var spec = RequirementValueParser.References("3 references of R2 million in the last 5 years");

        spec.Should().Be(new ReferenceSpec(3, 2_000_000m, 5));
    }

    [Fact]
    public void Reference_spec_parts_are_null_when_absent()
    {
        RequirementValueParser.References("contactable references").Should().Be(new ReferenceSpec(null, null, null));
    }
}
