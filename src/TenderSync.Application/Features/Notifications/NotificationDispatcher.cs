using Microsoft.Extensions.Logging;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Jobs;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Notifications;

namespace TenderSync.Application.Features.Notifications;

public sealed record DispatchResult(int Created, int Deduplicated, int Deferred, IReadOnlyList<Guid> NotificationIds);

/// <summary>
/// Turns one notification-worthy fact into rows (PLAN.md §7.6): one per enabled preference that wants the type, each rendered
/// for its channel, deduplicated by (type, tender, profile, channel, bucket) against notifications already stored, held
/// until the recipient's quiet hours end unless the fact is urgent, saved, and queued for sending. Runs in the Worker from
/// the outbox handlers and the digest job.
/// </summary>
public sealed class NotificationDispatcher(
    IAppDbContext db,
    NotificationTemplates templates,
    IPipelineScheduler scheduler,
    IDateTimeProvider clock,
    ILogger<NotificationDispatcher> logger)
{
    public async Task<DispatchResult> DispatchAsync(
        NotificationType type,
        NotificationModel model,
        Guid? tenderId,
        Guid? companyProfileId,
        string? bucket = null,
        bool urgent = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        var now = clock.UtcNow;
        var preferences = await db.NotificationPreferences.ListEnabledAsync(cancellationToken);
        var created = new List<Notification>();
        var deduplicated = 0;
        var deferred = 0;
        var payload = DomainJson.Serialize(new { actionUrl = NullIfEmpty(model.TenderUrl) ?? NullIfEmpty(model.DashboardUrl), actionLabel = NullIfEmpty(model.TenderUrl) is null ? "Open TenderSync" : "Open tender" });

        foreach (var preference in preferences.Where(p => p.Wants(type)))
        {
            var key = Notification.BuildDedupeKey(type, preference.Channel, preference.Address, tenderId, companyProfileId, bucket);
            if (created.Any(n => n.DedupeKey == key) || await db.Notifications.ExistsAsync(key, cancellationToken))
            {
                deduplicated++;
                continue;
            }

            var rendered = templates.Render(type, preference.Channel, model);
            DateTimeOffset? notBefore = urgent ? null : preference.QuietHours?.EndsAt(now);
            if (notBefore is not null)
            {
                deferred++;
            }

            created.Add(Notification.Create(type, preference.Channel, preference.Address, rendered.Subject, rendered.Body, tenderId, companyProfileId, bucket, payload, notBefore));
        }

        if (created.Count == 0)
        {
            logger.LogDebug("{Type} for tender {TenderId}: nothing to send ({Deduplicated} duplicate(s), {Preferences} enabled preference(s))", type, tenderId, deduplicated, preferences.Count);
            return new DispatchResult(0, deduplicated, 0, []);
        }

        foreach (var notification in created)
        {
            db.Notifications.Add(notification);
        }

        await db.SaveChangesAsync(cancellationToken);
        foreach (var notification in created.Where(n => n.NotBefore is null))
        {
            scheduler.EnqueueNotificationSend(notification.Id);
        }

        logger.LogInformation("{Type} for tender {TenderId}: {Created} notification(s) queued, {Deferred} held for quiet hours, {Deduplicated} duplicate(s)", type, tenderId, created.Count, deferred, deduplicated);
        return new DispatchResult(created.Count, deduplicated, deferred, created.Select(n => n.Id).ToList());
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
