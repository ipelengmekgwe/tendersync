using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Features.Notifications;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Status;

public sealed record ReminderSummary(int Candidates, int Created, int Deduplicated);

/// <summary>
/// The dated reminders (PLAN.md §7.6, §7.7): closing reminders in 7/3/1-day buckets for matched, non-dismissed tenders;
/// briefing reminders two days ahead (compulsory briefings also for dismissed matches); company documents expiring within
/// thirty days. Every reminder is deduplicated on the target's South African date, so a moved date reminds again and a
/// repeated run does not.
/// </summary>
public sealed class ReminderService(
    IAppDbContext db,
    IReadDb readDb,
    NotificationModelBuilder models,
    NotificationDispatcher dispatcher,
    IOptions<StatusOptions> options,
    IDateTimeProvider clock,
    ILogger<ReminderService> logger)
{
    private const string ClosingSql = """
        select t.id as tender_id, m.company_profile_id
        from tendersync.tender_matches m
        join tendersync.tenders t on t.id = m.tender_id
        where m.status <> @dismissed
          and t.status in (@open, @closingSoon)
          and t.closing_at > @now and t.closing_at <= @until
        order by t.closing_at
        """;

    private const string BriefingSql = """
        select t.id as tender_id, m.company_profile_id
        from tendersync.tender_matches m
        join tendersync.tenders t on t.id = m.tender_id
        where (m.status <> @dismissed or t.briefing_compulsory = true)
          and t.status not in (@closed, @awarded, @cancelled)
          and t.briefing_at > @now and t.briefing_at <= @until
        order by t.briefing_at
        """;

    private const string ExpiringSql = """
        select d.id as document_id, d.company_profile_id, d.title, d.expires_at
        from tendersync.company_documents d
        join tendersync.company_profiles p on p.id = d.company_profile_id
        where d.deleted_at is null and p.is_active
          and d.expires_at > @now and d.expires_at <= @until
        order by d.expires_at
        """;

    public async Task<ReminderSummary> SendClosingRemindersAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var rows = await readDb.QueryAsync<MatchRow>(ClosingSql, new
        {
            dismissed = (int)MatchStatus.Dismissed,
            open = (int)TenderStatus.Open,
            closingSoon = (int)TenderStatus.ClosingSoon,
            now,
            until = now.AddDays(8),
        }, cancellationToken);

        int created = 0, deduplicated = 0;
        foreach (var row in rows)
        {
            var tender = await db.Tenders.GetWithDetailsAsync(row.TenderId, cancellationToken);
            if (tender?.ClosingAt is not { } closing || ReminderBuckets.Closing(closing, now) is not { } type)
            {
                continue;
            }

            var profile = await db.CompanyProfiles.GetByIdAsync(row.CompanyProfileId, cancellationToken);
            var model = await models.ForTenderAsync(type, tender, profile, now, cancellationToken);
            var urgent = type == NotificationType.ClosingSoon1d;
            var result = await dispatcher.DispatchAsync(type, model, tender.Id, row.CompanyProfileId, ReminderBuckets.DateBucket(closing), urgent, cancellationToken);
            created += result.Created;
            deduplicated += result.Deduplicated;
        }

        logger.LogInformation("Closing reminders: {Candidates} match(es), {Created} message(s), {Deduplicated} already sent", rows.Count, created, deduplicated);
        return new ReminderSummary(rows.Count, created, deduplicated);
    }

    public async Task<ReminderSummary> SendBriefingRemindersAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var days = options.Value.BriefingReminderDays;
        var rows = await readDb.QueryAsync<MatchRow>(BriefingSql, new
        {
            dismissed = (int)MatchStatus.Dismissed,
            closed = (int)TenderStatus.Closed,
            awarded = (int)TenderStatus.Awarded,
            cancelled = (int)TenderStatus.Cancelled,
            now,
            until = now.AddDays(days + 1),
        }, cancellationToken);

        int created = 0, deduplicated = 0;
        foreach (var row in rows)
        {
            var tender = await db.Tenders.GetWithDetailsAsync(row.TenderId, cancellationToken);
            if (tender?.BriefingAt is not { } briefing || !ReminderBuckets.BriefingDue(briefing, now, days))
            {
                continue;
            }

            var profile = await db.CompanyProfiles.GetByIdAsync(row.CompanyProfileId, cancellationToken);
            var model = await models.ForTenderAsync(NotificationType.BriefingReminder, tender, profile, now, cancellationToken);
            var result = await dispatcher.DispatchAsync(NotificationType.BriefingReminder, model, tender.Id, row.CompanyProfileId, ReminderBuckets.DateBucket(briefing), tender.BriefingCompulsory == true, cancellationToken);
            created += result.Created;
            deduplicated += result.Deduplicated;
        }

        logger.LogInformation("Briefing reminders: {Candidates} match(es), {Created} message(s), {Deduplicated} already sent", rows.Count, created, deduplicated);
        return new ReminderSummary(rows.Count, created, deduplicated);
    }

    public async Task<ReminderSummary> SendDocumentExpiryAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var days = options.Value.DocumentExpiryDays;
        var rows = await readDb.QueryAsync<DocumentRow>(ExpiringSql, new { now, until = now.AddDays(days) }, cancellationToken);

        int created = 0, deduplicated = 0;
        foreach (var row in rows)
        {
            var model = new NotificationModel
            {
                Type = NotificationType.DocumentExpiring.ToString(),
                DocumentTitle = row.Title,
                ExpiresOn = row.ExpiresAt.ToOffset(ReminderBuckets.SastOffset).ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture),
                DaysLeft = Math.Max(ReminderBuckets.DaysUntil(row.ExpiresAt, now), 0),
                DashboardUrl = models.DashboardUrl,
            };
            var result = await dispatcher.DispatchAsync(NotificationType.DocumentExpiring, model, null, row.CompanyProfileId, $"{row.DocumentId:N}:{ReminderBuckets.DateBucket(row.ExpiresAt)}", false, cancellationToken);
            created += result.Created;
            deduplicated += result.Deduplicated;
        }

        logger.LogInformation("Document expiry: {Candidates} document(s) expiring within {Days} days, {Created} message(s), {Deduplicated} already sent", rows.Count, days, created, deduplicated);
        return new ReminderSummary(rows.Count, created, deduplicated);
    }

    private sealed class MatchRow
    {
        public Guid TenderId { get; set; }

        public Guid CompanyProfileId { get; set; }
    }

    private sealed class DocumentRow
    {
        public Guid DocumentId { get; set; }

        public Guid CompanyProfileId { get; set; }

        public string Title { get; set; } = string.Empty;

        public DateTimeOffset ExpiresAt { get; set; }
    }
}
