using CommunityManagement.Application.Meetings.Commands;
using CommunityManagement.Application.Meetings.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class MeetingEndpoints
{
    public static IEndpointRouteBuilder MapMeetingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{orgId:guid}/meetings")
            .RequireAuthorization();

        // GET / — Liste
        group.MapGet("/", async (
            Guid orgId, IMediator mediator,
            [FromQuery] string? status = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var result = await mediator.Send(new GetMeetingsQuery(orgId, status, page, pageSize));
            return Results.Ok(result);
        });

        // GET /{id} — Detay + bagli gundem maddeleri
        group.MapGet("/{id:guid}", async (Guid orgId, Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetMeetingDetailQuery(orgId, id));
            return Results.Ok(result);
        });

        // POST / — Olustur
        group.MapPost("/", async (Guid orgId, [FromBody] CreateMeetingRequest req, IMediator mediator) =>
        {
            var result = await mediator.Send(new CreateMeetingCommand(
                orgId, req.Title, req.Description, req.MeetingDate));
            return Results.Created($"/api/v1/organizations/{orgId}/meetings/{result.Id}", result);
        });

        // PUT /{id} — Guncelle
        group.MapPut("/{id:guid}", async (Guid orgId, Guid id, [FromBody] UpdateMeetingRequest req, IMediator mediator) =>
        {
            await mediator.Send(new UpdateMeetingCommand(orgId, id, req.Title, req.Description, req.MeetingDate));
            return Results.NoContent();
        });

        // PUT /{id}/status — Durum guncelle
        group.MapPut("/{id:guid}/status", async (Guid orgId, Guid id, [FromBody] UpdateMeetingStatusRequest req, IMediator mediator) =>
        {
            await mediator.Send(new UpdateMeetingStatusCommand(orgId, id, req.Status));
            return Results.NoContent();
        });

        // POST /{id}/agenda-items — Gundem maddeleri bagla
        group.MapPost("/{id:guid}/agenda-items", async (Guid orgId, Guid id, [FromBody] LinkAgendaRequest req, IMediator mediator) =>
        {
            await mediator.Send(new LinkAgendaToMeetingCommand(orgId, id, req.AgendaItemIds));
            return Results.NoContent();
        });

        return app;
    }

    public record CreateMeetingRequest(string Title, string? Description, DateTimeOffset MeetingDate);
    public record UpdateMeetingRequest(string Title, string? Description, DateTimeOffset MeetingDate);
    public record UpdateMeetingStatusRequest(string Status);
    public record LinkAgendaRequest(List<Guid> AgendaItemIds);
}
