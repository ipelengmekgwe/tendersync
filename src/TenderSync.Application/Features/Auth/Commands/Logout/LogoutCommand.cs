using FluentValidation;
using MediatR;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Identity;
using TenderSync.Application.Common.Auditing;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;

namespace TenderSync.Application.Features.Auth.Commands.Logout;

/// <summary>Revokes the presented refresh token. The access token simply expires.</summary>
[Authorize]
public sealed record LogoutCommand([property: Sensitive] string RefreshToken) : ICommand<Unit>;

public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        RuleFor(c => c.RefreshToken).NotEmpty().MaximumLength(512);
    }
}

public sealed class LogoutCommandHandler(ITokenService tokens, ICurrentUser currentUser) : IRequestHandler<LogoutCommand, Unit>
{
    public async Task<Unit> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        await tokens.RevokeAsync(request.RefreshToken, currentUser.IpAddress, cancellationToken);
        return Unit.Value;
    }
}
