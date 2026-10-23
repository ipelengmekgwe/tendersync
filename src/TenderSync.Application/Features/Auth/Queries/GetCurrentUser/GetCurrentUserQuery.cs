using MediatR;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Identity;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;

namespace TenderSync.Application.Features.Auth.Queries.GetCurrentUser;

[Authorize]
public sealed record GetCurrentUserQuery : IQuery<UserDto>;

public sealed class GetCurrentUserQueryHandler(ICurrentUser currentUser, IIdentityService identity) : IRequestHandler<GetCurrentUserQuery, UserDto>
{
    public async Task<UserDto> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var user = await identity.GetUserAsync(currentUser.UserId ?? throw new UnauthorizedException(), cancellationToken)
            ?? throw new UnauthorizedException("The user no longer exists.");

        return new UserDto(user.Id, user.Email, user.DisplayName, user.Roles);
    }
}
