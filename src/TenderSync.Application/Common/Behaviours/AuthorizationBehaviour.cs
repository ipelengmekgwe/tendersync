using System.Reflection;
using MediatR;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Security;

namespace TenderSync.Application.Common.Behaviours;

/// <summary>
/// Enforces [Authorize] / [AllowAnonymous] on request types. A request with neither attribute requires an authenticated user,
/// so forgetting the attribute fails closed.
/// </summary>
public sealed class AuthorizationBehaviour<TRequest, TResponse>(ICurrentUser currentUser) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var type = typeof(TRequest);
        if (type.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
        {
            return next(cancellationToken);
        }

        if (!currentUser.IsAuthenticated)
        {
            throw new UnauthorizedException();
        }

        var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
        if (authorize?.Role is { } role && !RoleHierarchy.Satisfies(currentUser.Roles, role))
        {
            throw new ForbiddenException($"{type.Name} requires the {role} role.");
        }

        return next(cancellationToken);
    }
}
