using CommunityManagement.Application.Agenda.Commands;
using CommunityManagement.Application.Agenda.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class AgendaEndpoints
{
    public static IEndpointRouteBuilder MapAgendaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{orgId:guid}/agenda-items")
            .RequireAuthorization();

        // GET / — Liste
        group.MapGet("/", async (
            Guid orgId, IMediator mediator,
            [FromQuery] string? status = null,
            [FromQuery] string? category = null,
            [FromQuery] Guid? meetingId = null,
            [FromQuery] string sortBy = "date",
            [FromQuery] string sortDir = "desc",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var result = await mediator.Send(new GetAgendaItemsQuery(
                orgId, status, category, meetingId,
                sortBy, sortDir, page, pageSize));
            return Results.Ok(result);
        });

        // GET /stats — Istatistikler
        group.MapGet("/stats", async (Guid orgId, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetAgendaStatsQuery(orgId));
            return Results.Ok(result);
        });

        // GET /{id} — Detay
        group.MapGet("/{id:guid}", async (Guid orgId, Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetAgendaItemDetailQuery(orgId, id));
            return Results.Ok(result);
        });

        // POST / — Olustur
        group.MapPost("/", async (Guid orgId, [FromBody] CreateAgendaRequest req, IMediator mediator) =>
        {
            var result = await mediator.Send(new CreateAgendaItemCommand(
                orgId, req.Title, req.Description, req.Category));
            return Results.Created($"/api/v1/organizations/{orgId}/agenda-items/{result.Id}", result);
        });

        // PUT /{id} — Guncelle
        group.MapPut("/{id:guid}", async (Guid orgId, Guid id, [FromBody] UpdateAgendaRequest req, IMediator mediator) =>
        {
            await mediator.Send(new UpdateAgendaItemCommand(orgId, id, req.Title, req.Description, req.Category));
            return Results.NoContent();
        });

        // DELETE /{id} — Sil
        group.MapDelete("/{id:guid}", async (Guid orgId, Guid id, IMediator mediator) =>
        {
            await mediator.Send(new DeleteAgendaItemCommand(orgId, id));
            return Results.NoContent();
        });

        // PUT /{id}/status — Durum guncelle
        group.MapPut("/{id:guid}/status", async (Guid orgId, Guid id, [FromBody] UpdateStatusRequest req, IMediator mediator) =>
        {
            await mediator.Send(new UpdateAgendaItemStatusCommand(orgId, id, req.Status, req.CloseReason));
            return Results.NoContent();
        });

        // PUT /{id}/pin — Sabitle toggle
        group.MapPut("/{id:guid}/pin", async (Guid orgId, Guid id, IMediator mediator) =>
        {
            await mediator.Send(new ToggleAgendaPinCommand(orgId, id));
            return Results.NoContent();
        });

        // POST /{id}/support — Destek toggle
        group.MapPost("/{id:guid}/support", async (Guid orgId, Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new ToggleAgendaSupportCommand(orgId, id));
            return Results.Ok(result);
        });

        // GET /{id}/supporters — Destekci listesi (admin)
        group.MapGet("/{id:guid}/supporters", async (Guid orgId, Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetAgendaSupportersQuery(orgId, id));
            return Results.Ok(result);
        });

        // GET /{id}/comments — Yorumlar
        group.MapGet("/{id:guid}/comments", async (
            Guid orgId, Guid id, IMediator mediator,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var result = await mediator.Send(new GetAgendaCommentsQuery(orgId, id, page, pageSize));
            return Results.Ok(result);
        });

        // POST /{id}/comments — Yorum ekle
        group.MapPost("/{id:guid}/comments", async (Guid orgId, Guid id, [FromBody] CreateCommentRequest req, IMediator mediator) =>
        {
            var result = await mediator.Send(new CreateAgendaCommentCommand(orgId, id, req.Content));
            return Results.Created($"/api/v1/organizations/{orgId}/agenda-items/{id}/comments/{result.Id}", result);
        });

        // DELETE /{id}/comments/{commentId} — Yorum sil
        group.MapDelete("/{id:guid}/comments/{commentId:guid}", async (Guid orgId, Guid id, Guid commentId, IMediator mediator) =>
        {
            await mediator.Send(new DeleteAgendaCommentCommand(orgId, id, commentId));
            return Results.NoContent();
        });

        return app;
    }

    public record CreateAgendaRequest(string Title, string? Description, string? Category);
    public record UpdateAgendaRequest(string Title, string? Description, string? Category);
    public record UpdateStatusRequest(string Status, string? CloseReason);
    public record CreateCommentRequest(string Content);
}
