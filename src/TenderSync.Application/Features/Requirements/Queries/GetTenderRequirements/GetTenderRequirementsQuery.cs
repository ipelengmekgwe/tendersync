using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Requirements.Queries.GetTenderRequirements;

public sealed record RequirementDto(
    Guid Id,
    RequirementKind Kind,
    RequirementType Type,
    string Text,
    string? ValueJson,
    bool Mandatory,
    decimal? Weight,
    decimal? Threshold,
    decimal Confidence,
    bool NeedsVerification,
    Guid? SourceDocumentId,
    string? SourceDocumentTitle,
    int? SourcePage,
    string? SourceQuote);

public sealed record TenderRequirementsDto(
    Guid TenderId,
    string? Summary,
    DateTimeOffset? ExtractedAt,
    string? Model,
    string? PromptVersion,
    int Total,
    int NeedsVerification,
    IReadOnlyList<RequirementDto> Mandatory,
    IReadOnlyList<RequirementDto> Functional,
    IReadOnlyList<RequirementDto> Documents,
    IReadOnlyList<RequirementDto> Other);

/// <summary>GET /tenders/{id}/requirements: the extracted requirements grouped for the "Can we bid?" page, each with its source and verify flag.</summary>
[Authorize(Roles.Viewer)]
public sealed record GetTenderRequirementsQuery(Guid TenderId) : IQuery<TenderRequirementsDto>;

public sealed class GetTenderRequirementsHandler(IAppDbContext db) : IRequestHandler<GetTenderRequirementsQuery, TenderRequirementsDto>
{
    public async Task<TenderRequirementsDto> Handle(GetTenderRequirementsQuery request, CancellationToken cancellationToken)
    {
        var tender = await db.Tenders.GetWithDetailsAsync(request.TenderId, cancellationToken)
            ?? throw new NotFoundException(nameof(Tender), request.TenderId);
        var rows = await db.Tenders.GetRequirementsAsync(tender.Id, cancellationToken);
        var titles = tender.Documents.ToDictionary(d => d.Id, d => d.Title);

        RequirementDto Map(TenderRequirement r) => new(
            r.Id, r.Kind, r.Type, r.Text, r.ValueJson, r.Mandatory, r.Weight, r.Threshold, r.Confidence, r.NeedsVerification,
            r.Source?.DocumentId, r.Source?.DocumentId is { } id && titles.TryGetValue(id, out var title) ? title : null, r.Source?.Page, r.Source?.Quote);

        var dtos = rows.Select(Map).ToList();
        var latest = rows.MaxBy(r => r.ExtractedAt);
        return new TenderRequirementsDto(
            tender.Id,
            tender.Summary,
            latest?.ExtractedAt,
            latest?.Model,
            latest?.PromptVersion,
            dtos.Count,
            dtos.Count(d => d.NeedsVerification),
            dtos.Where(d => d.Kind == RequirementKind.Mandatory).ToList(),
            dtos.Where(d => d.Kind is RequirementKind.Functional or RequirementKind.Evaluation).ToList(),
            dtos.Where(d => d.Kind == RequirementKind.Document).ToList(),
            dtos.Where(d => d.Kind is RequirementKind.Briefing or RequirementKind.Submission or RequirementKind.Pricing or RequirementKind.Contract or RequirementKind.Other).ToList());
    }
}
