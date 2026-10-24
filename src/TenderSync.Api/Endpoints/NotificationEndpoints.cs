using MediatR;
using TenderSync.Api.Security;
using TenderSync.Application.Common.Models;
using TenderSync.Application.Features.Notifications;
using TenderSync.Application.Features.Notifications.Commands.SendTestNotification;
using TenderSync.Application.Features.Notifications.Commands.UpdateNotificationPreferences;
using TenderSync.Application.Features.Notifications.Queries.GetNotificationPreferences;
using TenderSync.Application.Features.Notifications.Queries.ListMyNotifications;
using TenderSync.Domain.Enums;

namespace TenderSync.Api.Endpoints;

/// <summary>Notifications and preferences (PLAN.md §10): mine, my channel subscriptions, and a test send.</summary>
public static class NotificationEndpoints
{
    public sealed record TestRequest(NotificationChannel Channel);

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var notifications = app.MapGroup("/api/v1/notifications")
            .WithTags("Notifications")
            .RequireAuthorization(Policies.Viewer);

        notifications.MapGet("/", async (NotificationStatus? status, NotificationType? type, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new ListMyNotificationsQuery(status, type, page ?? 1, pageSize ?? Paging.DefaultPageSize), ct)))
            .WithSummary("Messages sent (or queued) to any of the caller's preference addresses, newest first.")
            .Produces<PagedResult<NotificationDto>>()
            .ProducesValidationProblem();

        notifications.MapPost("/test", async (TestRequest request, ISender sender, CancellationToken ct) =>
                TypedResults.Accepted((string?)null, await sender.Send(new SendTestNotificationCommand(request.Channel), ct)))
            .WithSummary("Queues a test message to the caller's address on the channel; the Worker delivers it within about a minute.")
            .Produces<TestNotificationResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var preferences = app.MapGroup("/api/v1/notification-preferences")
            .WithTags("Notifications")
            .RequireAuthorization(Policies.Viewer);

        preferences.MapGet("/", async (ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new GetNotificationPreferencesQuery(), ct)))
            .WithSummary("The caller's channel subscriptions.")
            .Produces<IReadOnlyList<NotificationPreferenceDto>>();

        preferences.MapPut("/", async (IReadOnlyList<NotificationPreferenceInput> input, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new UpdateNotificationPreferencesCommand(input), ct)))
            .WithSummary("Replaces the caller's subscriptions: one entry per channel with address, types, enabled flag and optional quiet hours. Channels left out are removed.")
            .Produces<IReadOnlyList<NotificationPreferenceDto>>()
            .ProducesValidationProblem();

        return app;
    }
}
