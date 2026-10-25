using FluentValidation;
using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Categories;

namespace TenderSync.Application.Features.Categories.Commands.CreateInterestCategory;

/// <summary>POST /interest-categories (Admin). Codes are upper-case letters, digits and underscores, unique. New tenders use it from the next classification.</summary>
[Authorize(Roles.Admin)]
public sealed record CreateInterestCategoryCommand(string Code, InterestCategoryInput Category) : ICommand<InterestCategoryDto>;

public sealed class CreateInterestCategoryCommandValidator : AbstractValidator<CreateInterestCategoryCommand>
{
    public CreateInterestCategoryCommandValidator()
    {
        RuleFor(c => c.Code).NotEmpty().Matches("^[A-Za-z][A-Za-z0-9_]{1,39}$")
            .WithMessage("Code: 2 to 40 letters, digits or underscores, starting with a letter.");
        RuleFor(c => c.Category).NotNull().SetValidator(new InterestCategoryInputValidator());
    }
}

public sealed class CreateInterestCategoryCommandHandler(IAppDbContext db) : IRequestHandler<CreateInterestCategoryCommand, InterestCategoryDto>
{
    public async Task<InterestCategoryDto> Handle(CreateInterestCategoryCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.InterestCategories.GetByCodeAsync(code, cancellationToken) is not null)
        {
            throw new ConflictException($"A category with code {code} already exists.");
        }

        var input = request.Category;
        var category = InterestCategory.Create(code, input.Name, input.Description, input.Keywords, input.NegativeKeywords, input.Weight);
        category.SetEnabled(input.Enabled);
        db.InterestCategories.Add(category);
        return InterestCategoryDto.From(category);
    }
}
