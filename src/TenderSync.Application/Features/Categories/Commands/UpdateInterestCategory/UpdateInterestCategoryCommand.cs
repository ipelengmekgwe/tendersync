using FluentValidation;
using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Auditing;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Categories;

namespace TenderSync.Application.Features.Categories.Commands.UpdateInterestCategory;

/// <summary>
/// PUT /interest-categories/{id} (Admin): replaces name, description, keywords, negative keywords, weight and enabled.
/// Existing classifications are not recomputed; reclassify a tender (or wait for its next change) to see the effect.
/// </summary>
[Authorize(Roles.Admin)]
public sealed record UpdateInterestCategoryCommand(Guid Id, InterestCategoryInput Category) : ICommand<InterestCategoryDto>, IAuditedEntityReference
{
    public string AuditEntityType => nameof(InterestCategory);

    public string? AuditEntityId => Id.ToString();
}

public sealed class UpdateInterestCategoryCommandValidator : AbstractValidator<UpdateInterestCategoryCommand>
{
    public UpdateInterestCategoryCommandValidator()
    {
        RuleFor(c => c.Category).NotNull().SetValidator(new InterestCategoryInputValidator());
    }
}

public sealed class UpdateInterestCategoryCommandHandler(IAppDbContext db) : IRequestHandler<UpdateInterestCategoryCommand, InterestCategoryDto>
{
    public async Task<InterestCategoryDto> Handle(UpdateInterestCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await db.InterestCategories.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(InterestCategory), request.Id);
        var input = request.Category;
        category.Update(input.Name, input.Description, input.Keywords, input.NegativeKeywords, input.Weight, input.Enabled);
        return InterestCategoryDto.From(category);
    }
}
