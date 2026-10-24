using MediatR;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Abstractions.Security;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Companies;

namespace TenderSync.Application.Features.Companies.Queries.ListCompanyDocuments;

/// <summary>GET /company-profiles/{id}/documents: the library with expiry flags; deleted entries only on request.</summary>
[Authorize(Roles.Viewer)]
public sealed record ListCompanyDocumentsQuery(Guid CompanyProfileId, bool IncludeDeleted = false) : IQuery<IReadOnlyList<CompanyDocumentDto>>;

public sealed class ListCompanyDocumentsHandler(IAppDbContext db, IEncryptionService encryption, IDateTimeProvider clock) : IRequestHandler<ListCompanyDocumentsQuery, IReadOnlyList<CompanyDocumentDto>>
{
    public async Task<IReadOnlyList<CompanyDocumentDto>> Handle(ListCompanyDocumentsQuery request, CancellationToken cancellationToken)
    {
        _ = await db.CompanyProfiles.GetByIdAsync(request.CompanyProfileId, cancellationToken)
            ?? throw new NotFoundException(nameof(CompanyProfile), request.CompanyProfileId);
        var documents = await db.CompanyProfiles.GetDocumentsAsync(request.CompanyProfileId, request.IncludeDeleted, cancellationToken);
        var now = clock.UtcNow;
        return documents.Select(d => CompanyDocumentDto.From(d, encryption.KeyIdOf(d.EncryptedDataKey), now)).ToList();
    }
}
