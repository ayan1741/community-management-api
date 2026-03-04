using CommunityManagement.Application.Notifications.Commands;
using CommunityManagement.Application.Notifications.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{orgId:guid}/notifications")
            .RequireAuthorization();

        // GET / — Bildirim listele
        group.MapGet("/", async (
            Guid orgId, IMediator mediator,
            [FromQuery] bool? isRead = null,
            [FromQuery] string? type = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var result = await mediator.Send(new GetNotificationsQuery(orgId, isRead, type, page, pageSize));
            return Results.Ok(result);
        });

        // GET /unread-count — Okunmamış sayısı
        group.MapGet("/unread-count", async (Guid orgId, IMediator mediator) =>
        {
            var count = await mediator.Send(new GetUnreadNotificationCountQuery(orgId));
            return Results.Ok(new { unreadCount = count });
        });

        // POST /mark-read — Toplu okundu işaretle
        group.MapPost("/mark-read", async (Guid orgId, [FromBody] MarkReadRequest req, IMediator mediator) =>
        {
            await mediator.Send(new MarkNotificationsReadCommand(orgId, req.NotificationIds));
            return Results.NoContent();
        });

        return app;
    }

    public record MarkReadRequest(List<Guid>? NotificationIds);
}
