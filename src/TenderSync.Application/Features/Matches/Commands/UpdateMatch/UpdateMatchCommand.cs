using FluentValidation;
using MediatR;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Matches.Commands.UpdateMatch;

/// <summary>
/// PATCH /matches/{id} (PLAN.md §10): moves a match through the workflow and keeps the reviewer's notes. Null fields are
/// left as they are; an empty <see cref="Notes"/> clears the notes. Dismissing needs a reason. Nothing here submits a bid
/// (ADR-015): Applying and Applied only record what a person decided and did.
/// </summary>
[Authorize(Roles.Reviewer)]
public sealed record UpdateMatchCommand(Guid Id, MatchStatus? Status = null, string? Notes = null, string? DismissReason = null) : ICommand<MatchDto>;

public sealed class UpdateMatchCommandValidator : AbstractValidator<UpdateMatchCommand>
{
    public UpdateMatchCommandValidator()
    {
        RuleFor(c => c).Must(c => c.Status is not null || c.Notes is not null).WithMessage("Send a status, notes, or both.");
        RuleFor(c => c.Status).IsInEnum().When(c => c.Status is not null);
        RuleFor(c => c.DismissReason).NotEmpty().WithMessage("A dismiss reason is required to dismiss a match.").When(c => c.Status == MatchStatus.Dismissed);
        RuleFor(c => c.DismissReason).Empty().WithMessage("A dismiss reason only goes with status Dismissed.").When(c => c.Status != MatchStatus.Dismissed);
        RuleFor(c => c.DismissReason).MaximumLength(1024);
        RuleFor(c => c.Notes).MaximumLength(4000);
    }
}

public sealed class UpdateMatchCommandHandler(IAppDbContext db, IDateTimeProvider clock) : IRequestHandler<UpdateMatchCommand, MatchDto>
{
    public async Task<MatchDto> Handle(UpdateMatchCommand request, CancellationToken cancellationToken)
    {
        var match = await db.TenderMatches.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(TenderMatch), request.Id);

        if (request.Status == MatchStatus.Dismissed)
        {
            match.Dismiss(request.DismissReason!, clock.UtcNow);
        }
        else if (request.Status is { } status && status != match.Status)
        {
            match.SetStatus(status);
        }

        if (request.Notes is not null)
        {
            match.SetNotes(request.Notes);
        }

        return MatchDto.From(match);
    }
}
