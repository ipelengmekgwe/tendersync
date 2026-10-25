namespace TenderSync.Evals;

public class EvalBudgetTests
{
    [Theory]
    [InlineData(null, "2")]
    [InlineData("", "2")]
    [InlineData("0.75", "0.75")]
    [InlineData("5", "5")]
    [InlineData("-1", "2")]
    [InlineData("lots", "2")]
    public void The_cap_comes_from_the_environment_or_defaults_to_two_dollars(string? value, string expected) =>
        EvalBudget.Parse(value).Should().Be(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
}
