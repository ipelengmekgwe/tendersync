using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Events;
using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Events;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Notifications.EventHandlers;

/// <summary>A new match (PLAN.md §7.6): NewMatch to everyone who wants it; urgent (no quiet hours) when the tender closes within two days.</summary>
public sealed class NotifyOnNewMatch(IAppDbContext db, NotificationModelBuilder models, NotificationDispatcher dispatcher, IOptions<NotificationOptions> options, IDateTimeProvider clock)
    : INotificationHandler<DomainEventNotification<TenderMatched>>
{
    public async Task Handle(DomainEventNotification<TenderMatched> notification, CancellationToken cancellationToken)
    {
        var evt = notification.Event;
        var tender = await db.Tenders.GetWithDetailsAsync(evt.TenderId, cancellationToken);
        if (tender is null)
        {
            return;
        }

        var profile = await db.CompanyProfiles.GetByIdAsync(evt.CompanyProfileId, cancellationToken);
        var now = clock.UtcNow;
        var model = await models.ForTenderAsync(NotificationType.NewMatch, tender, profile, now, cancellationToken);
        model.Relevance ??= evt.Relevance;
        var urgent = tender.ClosingAt is { } closing && closing <= now.AddDays(options.Value.UrgentClosingDays);
        await dispatcher.DispatchAsync(NotificationType.NewMatch, model, tender.Id, evt.CompanyProfileId, null, urgent, cancellationToken);
    }
}

/// <summary>A finished qualification run: QualificationReady, once per verdict (a re-run that changes the verdict notifies again).</summary>
public sealed class NotifyOnQualificationCompleted(IAppDbContext db, NotificationModelBuilder models, NotificationDispatcher dispatcher, IOptions<NotificationOptions> options, IDateTimeProvider clock)
    : INotificationHandler<DomainEventNotification<QualificationCompleted>>
{
    public async Task Handle(DomainEventNotification<QualificationCompleted> notification, CancellationToken cancellationToken)
    {
        var evt = notification.Event;
        var tender = await db.Tenders.GetWithDetailsAsync(evt.TenderId, cancellationToken);
        if (tender is null)
        {
            return;
        }

        var profile = await db.CompanyProfiles.GetByIdAsync(evt.CompanyProfileId, cancellationToken);
        var model = await models.ForTenderAsync(NotificationType.QualificationReady, tender, profile, clock.UtcNow, cancellationToken);
        var report = await db.QualificationReports.GetByIdAsync(evt.ReportId, cancellationToken);
        if (report is not null)
        {
            NotificationModelBuilder.Apply(model, report, options.Value.MaxMissingItems);
        }

        await dispatcher.DispatchAsync(NotificationType.QualificationReady, model, tender.Id, evt.CompanyProfileId, evt.Verdict.ToString().ToLowerInvariant(), false, cancellationToken);
    }
}

/// <summary>
/// A changed release of a matched tender: TenderChanged for new documents or a moved closing date (one message per distinct
/// change, keyed by a hash of the diff), AwardPublished when awards appear. Unmatched tenders stay quiet.
/// </summary>
public sealed class NotifyOnTenderChanged(IAppDbContext db, NotificationModelBuilder models, NotificationDispatcher dispatcher, IDateTimeProvider clock, ILogger<NotifyOnTenderChanged> logger)
    : INotificationHandler<DomainEventNotification<TenderChanged>>
{
    public async Task Handle(DomainEventNotification<TenderChanged> notification, CancellationToken cancellationToken)
    {
        var evt = notification.Event;
        var diff = evt.Diff;
        var newDocuments = diff.NewDocumentIds.Count > 0;
        if (!newDocuments && !diff.ClosingChanged && !diff.AwardsPublished)
        {
            return;
        }

        var matches = await db.TenderMatches.ListByTenderAsync(evt.TenderId, cancellationToken);
        if (matches.Count == 0)
        {
            return;
        }

        var tender = await db.Tenders.GetWithDetailsAsync(evt.TenderId, cancellationToken);
        if (tender is null)
        {
            return;
        }

        var now = clock.UtcNow;
        foreach (var match in matches.Where(m => m.Status != MatchStatus.Dismissed))
        {
            var profile = await db.CompanyProfiles.GetByIdAsync(match.CompanyProfileId, cancellationToken);
            if (newDocuments || diff.ClosingChanged)
            {
                var model = await models.ForTenderAsync(NotificationType.TenderChanged, tender, profile, now, cancellationToken);
                model.Change = Describe(diff, tender);
                await dispatcher.DispatchAsync(NotificationType.TenderChanged, model, tender.Id, match.CompanyProfileId, Bucket(diff), false, cancellationToken);
            }

            if (diff.AwardsPublished)
            {
                var model = await models.ForTenderAsync(NotificationType.AwardPublished, tender, profile, now, cancellationToken);
                model.Change = "The buyer published an award for this tender.";
                await dispatcher.DispatchAsync(NotificationType.AwardPublished, model, tender.Id, match.CompanyProfileId, null, false, cancellationToken);
            }
        }

        logger.LogDebug("Tender {Ocid} change notified to {Matches} profile(s)", evt.Ocid, matches.Count);
    }

    public static string Describe(ReleaseDiff diff, Tender tender)
    {
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(tender);
        var parts = new List<string>();
        if (diff.NewDocumentIds.Count > 0)
        {
            var titles = tender.Documents.Where(d => diff.NewDocumentIds.Contains(d.ExternalId, StringComparer.Ordinal)).Select(d => d.Title).Take(3).ToList();
            parts.Add($"{diff.NewDocumentIds.Count} new document{(diff.NewDocumentIds.Count == 1 ? string.Empty : "s")}{(titles.Count > 0 ? ": " + string.Join(", ", titles) : string.Empty)}");
        }

        if (diff.ClosingChanged)
        {
            parts.Add($"closing date moved from {(diff.OldClosingAt is { } old ? NotificationModel.Sast(old) : "not stated")} to {(diff.NewClosingAt is { } @new ? NotificationModel.Sast(@new) : "not stated")}");
        }

        return parts.Count == 0 ? "The release was updated." : string.Join("; ", parts) + ".";
    }

    /// <summary>A short hash of the diff: the same change replayed by a later ingest is not notified twice.</summary>
    public static string Bucket(ReleaseDiff diff)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(DomainJson.Serialize(diff)));
        return Convert.ToHexStringLower(hash)[..12];
    }
}

/// <summary>PLAN.md §7.7: Closed and Cancelled matched tenders are TenderChanged messages; Awarded is AwardPublished (deduplicated with the release diff path).</summary>
public sealed class NotifyOnStatusChanged(IAppDbContext db, NotificationModelBuilder models, NotificationDispatcher dispatcher, IDateTimeProvider clock)
    : INotificationHandler<DomainEventNotification<TenderStatusChanged>>
{
    public async Task Handle(DomainEventNotification<TenderStatusChanged> notification, CancellationToken cancellationToken)
    {
        var evt = notification.Event;
        if (evt.To is not (TenderStatus.Cancelled or TenderStatus.Awarded or TenderStatus.Closed))
        {
            return;
        }

        var matches = await db.TenderMatches.ListByTenderAsync(evt.TenderId, cancellationToken);
        if (matches.Count == 0)
        {
            return;
        }

        var tender = await db.Tenders.GetWithDetailsAsync(evt.TenderId, cancellationToken);
        if (tender is null)
        {
            return;
        }

        var type = evt.To == TenderStatus.Awarded ? NotificationType.AwardPublished : NotificationType.TenderChanged;
        foreach (var match in matches.Where(m => m.Status != MatchStatus.Dismissed))
        {
            var profile = await db.CompanyProfiles.GetByIdAsync(match.CompanyProfileId, cancellationToken);
            var model = await models.ForTenderAsync(type, tender, profile, clock.UtcNow, cancellationToken);
            model.Change = evt.To switch
            {
                TenderStatus.Cancelled => $"The tender was cancelled{(string.IsNullOrWhiteSpace(evt.Reason) ? string.Empty : ": " + evt.Reason)}.",
                TenderStatus.Closed => $"The tender closed{(tender.ClosingAt is { } closing ? " on " + NotificationModel.Sast(closing) : string.Empty)}.",
                _ => "The buyer published an award for this tender.",
            };
            var bucket = evt.To switch
            {
                TenderStatus.Cancelled => "cancelled",
                TenderStatus.Closed => "closed:" + (tender.ClosingAt is { } c ? Domain.Tenders.ReminderBuckets.DateBucket(c) : "-"),
                _ => null,
            };
            await dispatcher.DispatchAsync(type, model, tender.Id, match.CompanyProfileId, bucket, false, cancellationToken);
        }
    }
}

/// <summary>Pipeline trouble (PLAN.md §7.1, §8.4): one PipelineFailure per source per SAST day so a flapping source does not flood the channels.</summary>
public sealed class NotifyOnPipelineFailure(NotificationDispatcher dispatcher, IOptions<NotificationOptions> options, IDateTimeProvider clock)
    : INotificationHandler<DomainEventNotification<PipelineFailure>>
{
    public async Task Handle(DomainEventNotification<PipelineFailure> notification, CancellationToken cancellationToken)
    {
        var evt = notification.Event;
        var now = clock.UtcNow;
        var model = new NotificationModel
        {
            Type = NotificationType.PipelineFailure.ToString(),
            Source = evt.Source,
            Message = evt.Message,
            Date = NotificationModel.Sast(evt.OccurredAt),
            DashboardUrl = options.Value.DashboardBaseUrl,
        };
        var day = Common.SouthAfricaTime.ToLocal(now).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        await dispatcher.DispatchAsync(NotificationType.PipelineFailure, model, null, null, $"{evt.Source}:{day}", true, cancellationToken);
    }
}
