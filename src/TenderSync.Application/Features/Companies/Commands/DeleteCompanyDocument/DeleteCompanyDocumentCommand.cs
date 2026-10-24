using MediatR;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Auditing;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Companies;

namespace TenderSync.Application.Features.Companies.Commands.DeleteCompanyDocument;

/// <summary>DELETE /company-profiles/{id}/documents/{docId} (Admin): soft delete; the encrypted blob stays for the audit trail (ADR-013).</summary>
[Authorize(Roles.Admin)]
public sealed record DeleteCompanyDocumentCommand(Guid CompanyProfileId, Guid DocumentId) : ICommand<Unit>, IAuditedEntityReference
{
    public string AuditEntityType => nameof(CompanyDocument);

    public string? AuditEntityId => DocumentId.ToString();
}

public sealed class DeleteCompanyDocumentHandler(IAppDbContext db, IDateTimeProvider clock) : IRequestHandler<DeleteCompanyDocumentCommand, Unit>
{
    public async Task<Unit> Handle(DeleteCompanyDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await db.CompanyDocuments.GetByIdAsync(request.DocumentId, cancellationToken);
        if (document is null || document.CompanyProfileId != request.CompanyProfileId || document.IsDeleted)
        {
            throw new NotFoundException(nameof(CompanyDocument), request.DocumentId);
        }

        document.SoftDelete(clock.UtcNow);
        return Unit.Value;
    }
}
