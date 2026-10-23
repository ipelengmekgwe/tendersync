using FluentValidation;
using MediatR;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Identity;
using TenderSync.Application.Common.Auditing;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;

namespace TenderSync.Application.Features.Auth.Commands.Login;

[AllowAnonymous]
public sealed record LoginCommand(string Email, [property: Sensitive] string Password) : ICommand<AuthResponse>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(c => c.Password).NotEmpty().MaximumLength(256);
    }
}

public sealed class LoginCommandHandler(IIdentityService identity, ITokenService tokens, ICurrentUser currentUser, IDateTimeProvider clock)
    : IRequestHandler<LoginCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await identity.AuthenticateAsync(request.Email, request.Password, cancellationToken)
            ?? throw new UnauthorizedException("Invalid email or password.");

        var pair = await tokens.IssueAsync(user, currentUser.IpAddress, cancellationToken);
        await identity.RecordLoginAsync(user.Id, clock.UtcNow, cancellationToken);

        return AuthMapping.ToResponse(pair, user);
    }
}

internal static class AuthMapping
{
    public static AuthResponse ToResponse(TokenPair pair, AuthenticatedUser user) =>
        new(pair.AccessToken, pair.AccessTokenExpiresAt, pair.RefreshToken, pair.RefreshTokenExpiresAt, new UserDto(user.Id, user.Email, user.DisplayName, user.Roles));
}
