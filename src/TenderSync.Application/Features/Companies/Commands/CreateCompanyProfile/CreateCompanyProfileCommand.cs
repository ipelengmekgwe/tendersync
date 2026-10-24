using FluentValidation;
using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Companies;

namespace TenderSync.Application.Features.Companies.Commands.CreateCompanyProfile;

/// <summary>POST /company-profiles (Admin). Plural by design (ADR-016) although v1 runs with one profile.</summary>
[Authorize(Roles.Admin)]
public sealed record CreateCompanyProfileCommand(CompanyProfileInput Profile) : ICommand<CompanyProfileDto>;

public sealed class CreateCompanyProfileCommandValidator : AbstractValidator<CreateCompanyProfileCommand>
{
    public CreateCompanyProfileCommandValidator()
    {
        RuleFor(c => c.Profile).NotNull().SetValidator(new CompanyProfileInputValidator());
    }
}

public sealed class CreateCompanyProfileHandler(IAppDbContext db) : IRequestHandler<CreateCompanyProfileCommand, CompanyProfileDto>
{
    public Task<CompanyProfileDto> Handle(CreateCompanyProfileCommand request, CancellationToken cancellationToken)
    {
        var profile = CompanyProfile.Create(request.Profile.Name);
        request.Profile.ApplyTo(profile);
        db.CompanyProfiles.Add(profile);
        return Task.FromResult(CompanyProfileDto.From(profile, 0));
    }
}
