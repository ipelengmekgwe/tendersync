using System.Globalization;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common;
using TenderSync.Domain.Enums;

namespace TenderSync.Application.Features.Notifications;

public sealed record DigestOutcome(int NewMatches, int ClosingSoon, int Qualified, DispatchResult Dispatch);

/// <summary>
/// The 07:00 digest (PLAN.md §7.6, opt-in through the DailyDigest preference type): matches of the last 24 hours, matched
/// tenders closing within DigestClosingDays, and qualification verdicts of the last 24 hours. One message per day per channel.
/// </summary>
public sealed class DailyDigestBuilder(IReadDb readDb, NotificationDispatcher dispatcher, IOptions<NotificationOptions> options, IDateTimeProvider clock)
{
    private const string MatchesSql = """
        select t.id, t.bid_number, t.title, b.name as buyer, t.closing_at, m.relevance,
               (select r.verdict from tendersync.qualification_reports r where r.tender_id = t.id order by r.created_at desc limit 1) as verdict
        from tendersync.tender_matches m
        join tendersync.tenders t on t.id = m.tender_id
        join tendersync.buyers b on b.id = t.buyer_id
        where m.created_at >= @since and m.status <> @dismissed
        order by m.relevance desc, t.closing_at asc nulls last
        limit 25
        """;

    private const string ClosingSql = """
        select t.id, t.bid_number, t.title, b.name as buyer, t.closing_at, m.relevance,
               (select r.verdict from tendersync.qualification_reports r where r.tender_id = t.id order by r.created_at desc limit 1) as verdict
        from tendersync.tender_matches m
        join tendersync.tenders t on t.id = m.tender_id
        join tendersync.buyers b on b.id = t.buyer_id
        where t.closing_at >= @now and t.closing_at <= @until and m.status <> @dismissed
        order by t.closing_at asc
        limit 25
        """;

    private const string QualifiedSql = """
        select t.id, t.bid_number, t.title, b.name as buyer, t.closing_at, m.relevance, r.verdict
        from tendersync.qualification_reports r
        join tendersync.tenders t on t.id = r.tender_id
        join tendersync.buyers b on b.id = t.buyer_id
        left join tendersync.tender_matches m on m.tender_id = t.id and m.company_profile_id = r.company_profile_id
        where r.created_at >= @since
        order by r.created_at desc
        limit 25
        """;

    public async Task<DigestOutcome> SendAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var settings = options.Value;
        var since = now.AddHours(-24);
        var dismissed = (int)MatchStatus.Dismissed;

        var matches = await readDb.QueryAsync<Row>(MatchesSql, new { since, dismissed }, cancellationToken);
        var closing = await readDb.QueryAsync<Row>(ClosingSql, new { now, until = now.AddDays(settings.DigestClosingDays), dismissed }, cancellationToken);
        var qualified = await readDb.QueryAsync<Row>(QualifiedSql, new { since }, cancellationToken);

        var model = new NotificationModel
        {
            Type = NotificationType.DailyDigest.ToString(),
            Date = SouthAfricaTime.ToLocal(now).ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture),
            NewMatches = matches.Select(r => ToItem(r, now, settings)).ToList(),
            ClosingSoon = closing.Select(r => ToItem(r, now, settings)).ToList(),
            Qualified = qualified.Select(r => ToItem(r, now, settings)).ToList(),
            DashboardUrl = settings.DashboardBaseUrl,
        };

        var day = SouthAfricaTime.ToLocal(now).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var dispatch = await dispatcher.DispatchAsync(NotificationType.DailyDigest, model, null, null, day, false, cancellationToken);
        return new DigestOutcome(matches.Count, closing.Count, qualified.Count, dispatch);
    }

    private static DigestItem ToItem(Row row, DateTimeOffset now, NotificationOptions settings) => new(
        row.BidNumber, row.Title, row.Buyer,
        row.ClosingAt is { } closing ? NotificationModel.Sast(closing) : "not stated",
        NotificationModel.Relative(row.ClosingAt, now),
        row.Relevance,
        row.Verdict is { } verdict ? NotificationModelBuilder.VerdictText((QualificationVerdict)verdict) : "not assessed yet",
        settings.TenderUrl(row.Id));

    private sealed class Row
    {
        public Guid Id { get; set; }

        public string BidNumber { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Buyer { get; set; } = string.Empty;

        public DateTimeOffset? ClosingAt { get; set; }

        public int? Relevance { get; set; }

        public int? Verdict { get; set; }
    }
}
