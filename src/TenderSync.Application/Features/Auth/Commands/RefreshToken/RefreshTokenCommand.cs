using FluentValidation;
using MediatR;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Identity;
using TenderSync.Application.Common.Auditing;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Features.Auth.Commands.Login;
using TenderSync.Application.Security;

namespace TenderSync.Application.Features.Auth.Commands.RefreshToken;

[AllowAnonymous]
public sealed record RefreshTokenCommand([property: Sensitive] string RefreshToken) : ICommand<AuthResponse>;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(c => c.RefreshToken).NotEmpty().MaximumLength(512);
    }
}

public sealed class RefreshTokenCommandHandler(ITokenService tokens, ICurrentUser currentUser) : IRequestHandler<RefreshTokenCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var result = await tokens.RefreshAsync(request.RefreshToken, currentUser.IpAddress, cancellationToken)
            ?? throw new UnauthorizedException("The refresh token is invalid or has expired.");

        return AuthMapping.ToResponse(result.Tokens, result.User);
    }
}
