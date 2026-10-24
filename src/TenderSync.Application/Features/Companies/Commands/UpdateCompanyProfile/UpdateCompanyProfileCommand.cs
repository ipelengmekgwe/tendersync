using FluentValidation;
using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Auditing;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Companies;

namespace TenderSync.Application.Features.Companies.Commands.UpdateCompanyProfile;

/// <summary>PUT /company-profiles/{id} (Admin): replaces every editable section of the profile.</summary>
[Authorize(Roles.Admin)]
public sealed record UpdateCompanyProfileCommand(Guid Id, CompanyProfileInput Profile) : ICommand<CompanyProfileDto>, IAuditedEntityReference
{
    public string AuditEntityType => nameof(CompanyProfile);

    public string? AuditEntityId => Id.ToString();
}

public sealed class UpdateCompanyProfileCommandValidator : AbstractValidator<UpdateCompanyProfileCommand>
{
    public UpdateCompanyProfileCommandValidator()
    {
        RuleFor(c => c.Profile).NotNull().SetValidator(new CompanyProfileInputValidator());
    }
}

public sealed class UpdateCompanyProfileHandler(IAppDbContext db) : IRequestHandler<UpdateCompanyProfileCommand, CompanyProfileDto>
{
    public async Task<CompanyProfileDto> Handle(UpdateCompanyProfileCommand request, CancellationToken cancellationToken)
    {
        var profile = await db.CompanyProfiles.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(CompanyProfile), request.Id);
        request.Profile.ApplyTo(profile);
        var documents = await db.CompanyProfiles.CountDocumentsAsync(profile.Id, cancellationToken);
        return CompanyProfileDto.From(profile, documents);
    }
}
