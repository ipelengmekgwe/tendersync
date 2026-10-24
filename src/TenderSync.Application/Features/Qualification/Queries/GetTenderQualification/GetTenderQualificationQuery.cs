using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Qualification;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Qualification.Queries.GetTenderQualification;

public sealed record QualificationReportDto(
    Guid Id,
    Guid TenderId,
    Guid CompanyProfileId,
    string CompanyName,
    QualificationVerdict Verdict,
    int Score,
    int? EstimatedFunctionalityScore,
    IReadOnlyList<QualificationFinding> Findings,
    IReadOnlyList<MissingItem> MissingMandatory,
    IReadOnlyList<MissingItem> MissingDocuments,
    IReadOnlyList<FunctionalityScore> FunctionalityBreakdown,
    IReadOnlyList<HumanQuestion> Questions,
    string SummaryMarkdown,
    string Model,
    string PromptVersion,
    DateTimeOffset CreatedAt);

/// <summary>GET /tenders/{id}/qualification: the newest report (for one profile with companyProfileId). 404 until a run has completed.</summary>
[Authorize(Roles.Viewer)]
public sealed record GetTenderQualificationQuery(Guid TenderId, Guid? CompanyProfileId = null) : IQuery<QualificationReportDto>;

public sealed class GetTenderQualificationHandler(IAppDbContext db) : IRequestHandler<GetTenderQualificationQuery, QualificationReportDto>
{
    public async Task<QualificationReportDto> Handle(GetTenderQualificationQuery request, CancellationToken cancellationToken)
    {
        _ = await db.Tenders.GetByIdAsync(request.TenderId, cancellationToken)
            ?? throw new NotFoundException(nameof(Tender), request.TenderId);
        var report = await db.QualificationReports.GetLatestAsync(request.TenderId, request.CompanyProfileId, cancellationToken)
            ?? throw new NotFoundException($"No qualification report exists for tender '{request.TenderId}' yet; POST /tenders/{request.TenderId}/qualify to run one.");
        var profile = await db.CompanyProfiles.GetByIdAsync(report.CompanyProfileId, cancellationToken);

        return new QualificationReportDto(
            report.Id, report.TenderId, report.CompanyProfileId, profile?.Name ?? "unknown", report.Verdict, report.Score, report.EstimatedFunctionalityScore,
            report.Findings, report.MissingMandatory, report.MissingDocuments, report.FunctionalityBreakdown, report.Questions,
            report.SummaryMarkdown, report.Model, report.PromptVersion, report.CreatedAt);
    }
}
