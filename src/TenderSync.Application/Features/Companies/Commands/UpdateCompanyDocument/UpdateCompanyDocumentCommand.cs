using FluentValidation;
using MediatR;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Abstractions.Security;
using TenderSync.Application.Common.Auditing;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Companies;

namespace TenderSync.Application.Features.Companies.Commands.UpdateCompanyDocument;

/// <summary>PUT /company-profiles/{id}/documents/{docId} (Reviewer): title and expiry; the content itself is immutable (upload a new one).</summary>
[Authorize(Roles.Reviewer)]
public sealed record UpdateCompanyDocumentCommand(Guid CompanyProfileId, Guid DocumentId, string Title, DateTimeOffset? ExpiresAt) : ICommand<CompanyDocumentDto>, IAuditedEntityReference
{
    public string AuditEntityType => nameof(CompanyDocument);

    public string? AuditEntityId => DocumentId.ToString();
}

public sealed class UpdateCompanyDocumentCommandValidator : AbstractValidator<UpdateCompanyDocumentCommand>
{
    public UpdateCompanyDocumentCommandValidator()
    {
        RuleFor(c => c.Title).NotEmpty().MaximumLength(512);
    }
}

public sealed class UpdateCompanyDocumentHandler(IAppDbContext db, IEncryptionService encryption, IDateTimeProvider clock) : IRequestHandler<UpdateCompanyDocumentCommand, CompanyDocumentDto>
{
    public async Task<CompanyDocumentDto> Handle(UpdateCompanyDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await db.CompanyDocuments.GetByIdAsync(request.DocumentId, cancellationToken);
        if (document is null || document.CompanyProfileId != request.CompanyProfileId || document.IsDeleted)
        {
            throw new NotFoundException(nameof(CompanyDocument), request.DocumentId);
        }

        document.Rename(request.Title);
        document.SetExpiry(request.ExpiresAt);
        return CompanyDocumentDto.From(document, encryption.KeyIdOf(document.EncryptedDataKey), clock.UtcNow);
    }
}
