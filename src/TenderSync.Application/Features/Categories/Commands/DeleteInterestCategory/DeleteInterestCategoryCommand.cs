using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Auditing;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Categories;

namespace TenderSync.Application.Features.Categories.Commands.DeleteInterestCategory;

/// <summary>
/// DELETE /interest-categories/{id} (Admin). Past classifications keep the code as text; disabling (PUT enabled=false)
/// is the reversible alternative.
/// </summary>
[Authorize(Roles.Admin)]
public sealed record DeleteInterestCategoryCommand(Guid Id) : ICommand<Unit>, IAuditedEntityReference
{
    public string AuditEntityType => nameof(InterestCategory);

    public string? AuditEntityId => Id.ToString();
}

public sealed class DeleteInterestCategoryCommandHandler(IAppDbContext db) : IRequestHandler<DeleteInterestCategoryCommand, Unit>
{
    public async Task<Unit> Handle(DeleteInterestCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await db.InterestCategories.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(InterestCategory), request.Id);
        db.InterestCategories.Remove(category);
        return Unit.Value;
    }
}
