using MediatR;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Jobs;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Auditing;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Events;
using TenderSync.Domain.Notifications;

namespace TenderSync.Application.Features.Admin.Commands.RunJobNow;

public sealed record RunJobNowResponse(string JobId, DateTimeOffset RequestedAt);

/// <summary>
/// POST /admin/jobs/{id}/run (Admin): asks the Worker to run a recurring job now. The API cannot start jobs itself, so it
/// writes <see cref="JobRunRequested"/> to the outbox; the Worker's outbox job (every minute) triggers the job, so it
/// starts within about a minute and its run appears under job runs.
/// </summary>
[Authorize(Roles.Admin)]
public sealed record RunJobNowCommand(string JobId) : ICommand<RunJobNowResponse>, IAuditedEntityReference
{
    public string AuditEntityType => "RecurringJob";

    public string? AuditEntityId => JobId;
}

public sealed class RunJobNowCommandHandler(IRecurringJobCatalog catalog, IAppDbContext db, ICurrentUser user, IDateTimeProvider clock)
    : IRequestHandler<RunJobNowCommand, RunJobNowResponse>
{
    public async Task<RunJobNowResponse> Handle(RunJobNowCommand request, CancellationToken cancellationToken)
    {
        var jobs = await catalog.ListAsync(cancellationToken);
        var job = jobs.FirstOrDefault(j => string.Equals(j.Id, request.JobId, StringComparison.Ordinal))
            ?? throw new NotFoundException("RecurringJob", request.JobId);

        var now = clock.UtcNow;
        db.Outbox.Add(OutboxMessage.FromEvent(new JobRunRequested(job.Id, user.UserId, user.DisplayName, now)));
        return new RunJobNowResponse(job.Id, now);
    }
}
