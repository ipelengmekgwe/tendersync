using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Tenders.Queries.GetTender;

[Authorize(Roles.Viewer)]
public sealed record GetTenderQuery(Guid Id) : IQuery<TenderDetailDto>;

public sealed class GetTenderQueryHandler(IAppDbContext db) : IRequestHandler<GetTenderQuery, TenderDetailDto>
{
    public async Task<TenderDetailDto> Handle(GetTenderQuery request, CancellationToken cancellationToken)
    {
        var tender = await db.Tenders.GetWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Tender), request.Id);

        var classification = await db.Tenders.GetClassificationAsync(tender.Id, cancellationToken);

        return new TenderDetailDto(
            tender.Id,
            tender.Ocid,
            tender.BidNumber,
            tender.Title,
            tender.Description,
            tender.BuyerId,
            tender.Buyer?.Name ?? string.Empty,
            tender.Buyer?.Type ?? Domain.Enums.BuyerType.Unknown,
            tender.Province,
            tender.MainCategory,
            tender.AdditionalCategories,
            tender.ProcurementMethod,
            tender.ProcurementMethodDetails,
            tender.ValueAmount,
            tender.ValueCurrency,
            tender.PublishedAt,
            tender.ClosingAt,
            tender.BriefingAt,
            tender.BriefingCompulsory,
            tender.BriefingVenue,
            tender.SubmissionMethod,
            tender.Status,
            tender.StatusReason,
            tender.SourceUrl,
            tender.HasAwards,
            tender.FirstSeenAt,
            tender.LastSeenAt,
            tender.Summary,
            tender.Documents.OrderBy(d => d.DatePublished).ThenBy(d => d.Title)
                .Select(TenderDocumentDto.From).ToList(),
            classification is null ? null : new TenderClassificationDto(classification.Stage, classification.CategoryCodes, classification.Relevance, classification.Rationale, classification.ClassifiedAt),
            tender.StatusHistory.OrderBy(h => h.At).Select(h => new TenderStatusHistoryDto(h.FromStatus, h.ToStatus, h.Reason, h.At)).ToList());
    }
}
