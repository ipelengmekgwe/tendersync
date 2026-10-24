using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Classification.Queries.TestInterestCategories;

/// <summary>
/// POST /interest-categories/test (Admin): dry-runs the keyword stage against the current enabled categories, either for a
/// stored tender (TenderId) or for free text, so category keywords can be tuned without waiting for the next ingest.
/// </summary>
[Authorize(Roles.Admin)]
public sealed record TestInterestCategoriesQuery(
    Guid? TenderId = null,
    string? Title = null,
    string? Description = null,
    IReadOnlyList<string>? DocumentTitles = null,
    ProcurementCategory? MainCategory = null,
    string? ProcurementMethodDetails = null) : IQuery<PrefilterResult>;

public sealed class TestInterestCategoriesQueryValidator : AbstractValidator<TestInterestCategoriesQuery>
{
    public TestInterestCategoriesQueryValidator()
    {
        RuleFor(q => q).Must(q => q.TenderId is not null || !string.IsNullOrWhiteSpace(q.Title) || !string.IsNullOrWhiteSpace(q.Description))
            .WithMessage("Give a tenderId or a title/description to test.");
        RuleFor(q => q.Title).MaximumLength(1024);
        RuleFor(q => q.Description).MaximumLength(20_000);
    }
}

public sealed class TestInterestCategoriesQueryHandler(IAppDbContext db, KeywordPrefilter prefilter, IOptions<ClassificationOptions> options) : IRequestHandler<TestInterestCategoriesQuery, PrefilterResult>
{
    public async Task<PrefilterResult> Handle(TestInterestCategoriesQuery request, CancellationToken cancellationToken)
    {
        var categories = await db.InterestCategories.ListAsync(enabledOnly: true, cancellationToken);
        TenderText text;
        if (request.TenderId is { } tenderId)
        {
            var tender = await db.Tenders.GetWithDetailsAsync(tenderId, cancellationToken) ?? throw new NotFoundException(nameof(Tender), tenderId);
            text = TenderText.From(tender, options.Value.MaxDocumentTitles);
        }
        else
        {
            text = new TenderText(string.Empty, request.Title ?? string.Empty, request.Description ?? string.Empty, request.DocumentTitles ?? [], request.MainCategory ?? ProcurementCategory.Unknown, request.ProcurementMethodDetails);
        }

        return prefilter.Evaluate(text, categories);
    }
}
