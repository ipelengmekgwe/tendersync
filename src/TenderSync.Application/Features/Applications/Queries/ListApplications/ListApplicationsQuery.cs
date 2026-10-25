using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Enums;

namespace TenderSync.Application.Features.Applications.Queries.ListApplications;

public sealed record ApplicationListItemDto(
    Guid Id,
    Guid TenderId,
    string BidNumber,
    string Title,
    DateTimeOffset? ClosingAt,
    Guid CompanyProfileId,
    string CompanyName,
    ApplicationStatus Status,
    DateTimeOffset? SubmittedAt,
    string? SubmissionReference,
    DateTimeOffset CreatedAt);

/// <summary>
/// GET /applications (Reviewer): bid applications, newest first. v1 only creates the table (PLAN.md §16); Phase 2 adds the
/// create/generate/package commands, so this list is empty until then and the dashboard shows it as a placeholder.
/// </summary>
[Authorize(Roles.Reviewer)]
public sealed record ListApplicationsQuery : IQuery<IReadOnlyList<ApplicationListItemDto>>;

public sealed class ListApplicationsQueryHandler(IReadDb db) : IRequestHandler<ListApplicationsQuery, IReadOnlyList<ApplicationListItemDto>>
{
    private const string Sql = """
        select a.id, a.tender_id, t.bid_number, t.title, t.closing_at, a.company_profile_id, p.name as company_name,
               a.status, a.submitted_at, a.submission_reference, a.created_at
        from tendersync.applications a
        join tendersync.tenders t on t.id = a.tender_id
        join tendersync.company_profiles p on p.id = a.company_profile_id
        order by a.created_at desc
        limit 200
        """;

    public async Task<IReadOnlyList<ApplicationListItemDto>> Handle(ListApplicationsQuery request, CancellationToken cancellationToken)
    {
        var rows = await db.QueryAsync<Row>(Sql, null, cancellationToken);
        return rows.Select(r => new ApplicationListItemDto(
            r.Id, r.TenderId, r.BidNumber, r.Title, r.ClosingAt, r.CompanyProfileId, r.CompanyName, (ApplicationStatus)r.Status,
            r.SubmittedAt, r.SubmissionReference, r.CreatedAt)).ToList();
    }

    private sealed class Row
    {
        public Guid Id { get; init; }

        public Guid TenderId { get; init; }

        public string BidNumber { get; init; } = string.Empty;

        public string Title { get; init; } = string.Empty;

        public DateTimeOffset? ClosingAt { get; init; }

        public Guid CompanyProfileId { get; init; }

        public string CompanyName { get; init; } = string.Empty;

        public int Status { get; init; }

        public DateTimeOffset? SubmittedAt { get; init; }

        public string? SubmissionReference { get; init; }

        public DateTimeOffset CreatedAt { get; init; }
    }
}
