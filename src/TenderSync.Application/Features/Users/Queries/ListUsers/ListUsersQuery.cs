using MediatR;
using TenderSync.Application.Abstractions.Identity;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;

namespace TenderSync.Application.Features.Users.Queries.ListUsers;

/// <summary>GET /admin/users (Admin): every account with its role, disabled flag, lockout and last sign-in.</summary>
[Authorize(Roles.Admin)]
public sealed record ListUsersQuery : IQuery<IReadOnlyList<UserAccount>>;

public sealed class ListUsersQueryHandler(IIdentityService identity) : IRequestHandler<ListUsersQuery, IReadOnlyList<UserAccount>>
{
    public Task<IReadOnlyList<UserAccount>> Handle(ListUsersQuery request, CancellationToken cancellationToken) =>
        identity.ListUsersAsync(cancellationToken);
}
