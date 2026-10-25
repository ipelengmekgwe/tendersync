using FluentValidation;
using MediatR;
using TenderSync.Application.Abstractions.Identity;
using TenderSync.Application.Common.Auditing;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;

namespace TenderSync.Application.Features.Users.Commands.CreateUser;

/// <summary>POST /admin/users (Admin): a new account with one role. The password follows the Identity policy (12+ characters).</summary>
[Authorize(Roles.Admin)]
public sealed record CreateUserCommand(string Email, string DisplayName, string Role, [property: Sensitive] string Password) : ICommand<UserAccount>;

public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Role).Must(r => Roles.All.Contains(r)).WithMessage($"Role must be one of {string.Join(", ", Roles.All)}.");
        RuleFor(c => c.Password).NotEmpty().MinimumLength(12).MaximumLength(256);
    }
}

public sealed class CreateUserCommandHandler(IIdentityService identity) : IRequestHandler<CreateUserCommand, UserAccount>
{
    public Task<UserAccount> Handle(CreateUserCommand request, CancellationToken cancellationToken) =>
        identity.CreateUserAsync(request.Email.Trim(), request.DisplayName.Trim(), request.Role, request.Password, cancellationToken);
}
