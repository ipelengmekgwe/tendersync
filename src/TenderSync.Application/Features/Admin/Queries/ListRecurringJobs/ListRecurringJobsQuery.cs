using MediatR;
using TenderSync.Application.Abstractions.Jobs;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;

namespace TenderSync.Application.Features.Admin.Queries.ListRecurringJobs;

/// <summary>GET /admin/jobs (Admin): the recurring jobs the Worker registered, with cron, next and last run. Empty until the Worker has started once.</summary>
[Authorize(Roles.Admin)]
public sealed record ListRecurringJobsQuery : IQuery<IReadOnlyList<RecurringJobInfo>>;

public sealed class ListRecurringJobsQueryHandler(IRecurringJobCatalog catalog) : IRequestHandler<ListRecurringJobsQuery, IReadOnlyList<RecurringJobInfo>>
{
    public Task<IReadOnlyList<RecurringJobInfo>> Handle(ListRecurringJobsQuery request, CancellationToken cancellationToken) =>
        catalog.ListAsync(cancellationToken);
}
