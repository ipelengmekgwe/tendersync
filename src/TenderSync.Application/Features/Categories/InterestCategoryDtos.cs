using FluentValidation;
using TenderSync.Domain.Categories;

namespace TenderSync.Application.Features.Categories;

public sealed record InterestCategoryDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> NegativeKeywords,
    int Weight,
    bool Enabled,
    DateTimeOffset UpdatedAt)
{
    public static InterestCategoryDto From(InterestCategory c) =>
        new(c.Id, c.Code, c.Name, c.Description, c.Keywords, c.NegativeKeywords, c.Weight, c.Enabled, c.UpdatedAt);
}

/// <summary>The editable fields of a category (PUT body, and POST with a code).</summary>
public sealed record InterestCategoryInput(
    string Name,
    string? Description,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> NegativeKeywords,
    int Weight,
    bool Enabled);

public sealed class InterestCategoryInputValidator : AbstractValidator<InterestCategoryInput>
{
    public InterestCategoryInputValidator()
    {
        RuleFor(i => i.Name).NotEmpty().MaximumLength(200);
        RuleFor(i => i.Description).MaximumLength(2000);
        RuleFor(i => i.Weight).InclusiveBetween(1, 10);
        RuleFor(i => i.Keywords).NotNull().Must(k => k.Count <= 300).WithMessage("At most 300 keywords.");
        RuleFor(i => i.NegativeKeywords).NotNull().Must(k => k.Count <= 300).WithMessage("At most 300 negative keywords.");
        RuleForEach(i => i.Keywords).NotEmpty().MaximumLength(100);
        RuleForEach(i => i.NegativeKeywords).NotEmpty().MaximumLength(100);
    }
}
