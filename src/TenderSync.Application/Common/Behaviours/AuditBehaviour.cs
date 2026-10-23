using System.Reflection;
using MediatR;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Auditing;
using TenderSync.Application.Common.Messaging;
using TenderSync.Domain.Operations;

namespace TenderSync.Application.Common.Behaviours;

/// <summary>
/// Writes an audit_logs row for every successful command (PLAN.md §12), with [Sensitive] properties redacted.
/// Registered inside the unit-of-work behaviour so the row is saved with the command's own changes.
/// </summary>
public sealed class AuditBehaviour<TRequest, TResponse>(IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next(cancellationToken);

        if (request is not ICommand<TResponse> || typeof(TRequest).GetCustomAttribute<SkipAuditAttribute>() is not null)
        {
            return response;
        }

        var reference = request as IAuditedEntityReference;
        db.AuditLogs.Add(AuditLog.Record(
            actorUserId: currentUser.UserId,
            actorName: currentUser.DisplayName,
            action: typeof(TRequest).Name,
            entityType: reference?.AuditEntityType ?? typeof(TRequest).Name,
            entityId: reference?.AuditEntityId,
            beforeJson: null,
            afterJson: RequestRedactor.ToJson(request),
            ip: currentUser.IpAddress,
            at: clock.UtcNow));

        return response;
    }
}
