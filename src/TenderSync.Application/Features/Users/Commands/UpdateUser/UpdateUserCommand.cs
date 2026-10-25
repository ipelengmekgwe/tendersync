using FluentValidation;
using MediatR;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Identity;
using TenderSync.Application.Common.Auditing;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;

namespace TenderSync.Application.Features.Users.Commands.UpdateUser;

/// <summary>
/// PUT /admin/users/{id} (Admin): display name, role, disabled, and optionally a new password. An admin cannot disable or
/// demote themselves, and the last enabled admin cannot be disabled or demoted, so the system always keeps one way in.
/// </summary>
[Authorize(Roles.Admin)]
public sealed record UpdateUserCommand(Guid Id, string DisplayName, string Role, bool Disabled, [property: Sensitive] string? NewPassword = null)
    : ICommand<UserAccount>, IAuditedEntityReference
{
    public string AuditEntityType => "User";

    public string? AuditEntityId => Id.ToString();
}

public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Role).Must(r => Roles.All.Contains(r)).WithMessage($"Role must be one of {string.Join(", ", Roles.All)}.");
        RuleFor(c => c.NewPassword).MinimumLength(12).MaximumLength(256).When(c => !string.IsNullOrEmpty(c.NewPassword));
    }
}

public sealed class UpdateUserCommandHandler(IIdentityService identity, ICurrentUser currentUser) : IRequestHandler<UpdateUserCommand, UserAccount>
{
    public async Task<UserAccount> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        var users = await identity.ListUsersAsync(cancellationToken);
        var target = users.FirstOrDefault(u => u.Id == request.Id) ?? throw new NotFoundException("User", request.Id);
        var losesAdmin = target.Roles.Contains(Roles.Admin) && !target.Disabled && (request.Disabled || request.Role != Roles.Admin);

        if (losesAdmin && target.Id == currentUser.UserId)
        {
            throw new ConflictException("You cannot disable or demote your own account. Ask another admin.");
        }

        if (losesAdmin && users.Count(u => u.Roles.Contains(Roles.Admin) && !u.Disabled) <= 1)
        {
            throw new ConflictException("This is the last enabled admin. Make another account an admin first.");
        }

        return await identity.UpdateUserAsync(request.Id, request.DisplayName.Trim(), request.Role, request.Disabled, request.NewPassword, cancellationToken)
            ?? throw new NotFoundException("User", request.Id);
    }
}
