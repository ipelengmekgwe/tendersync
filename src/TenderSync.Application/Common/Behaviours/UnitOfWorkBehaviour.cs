using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Messaging;

namespace TenderSync.Application.Common.Behaviours;

/// <summary>Saves the unit of work after a command handler succeeds, so handlers never call SaveChangesAsync themselves. Queries pass through.</summary>
public sealed class UnitOfWorkBehaviour<TRequest, TResponse>(IAppDbContext db) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next(cancellationToken);

        if (request is ICommand<TResponse>)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return response;
    }
}
