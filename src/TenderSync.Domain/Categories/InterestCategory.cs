using TenderSync.Domain.Common;

namespace TenderSync.Domain.Categories;

/// <summary>A configurable area of interest used by the keyword prefilter and the LLM classifier (PLAN.md Appendix B).</summary>
public sealed class InterestCategory : Entity
{
    private InterestCategory()
    {
    }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public string[] Keywords { get; private set; } = [];

    public string[] NegativeKeywords { get; private set; } = [];

    public bool Enabled { get; private set; } = true;

    public int Weight { get; private set; } = 1;

    public static InterestCategory Create(string code, string name, string? description, IEnumerable<string> keywords, IEnumerable<string> negativeKeywords, int weight = 1)
    {
        var category = new InterestCategory
        {
            Code = Guard.NotEmpty(code, nameof(code)).ToUpperInvariant(),
        };
        category.Update(name, description, keywords, negativeKeywords, weight, enabled: true);
        return category;
    }

    public void Update(string name, string? description, IEnumerable<string> keywords, IEnumerable<string> negativeKeywords, int weight, bool enabled)
    {
        Name = Guard.NotEmpty(name, nameof(name));
        Description = Guard.Trimmed(description);
        Keywords = Normalise(keywords);
        NegativeKeywords = Normalise(negativeKeywords);
        Weight = Guard.InRange(weight, 1, 10, nameof(weight));
        Enabled = enabled;
    }

    public void SetEnabled(bool enabled) => Enabled = enabled;

    private static string[] Normalise(IEnumerable<string> values) =>
        [.. values.Select(v => v.Trim().ToLowerInvariant()).Where(v => v.Length > 0).Distinct(StringComparer.Ordinal)];
}
