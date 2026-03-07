using CommunityManagement.Application.Decisions.Commands;
using CommunityManagement.Application.Decisions.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class DecisionEndpoints
{
    public static IEndpointRouteBuilder MapDecisionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{orgId:guid}/decisions")
            .RequireAuthorization();

        // GET / — Liste
        group.MapGet("/", async (
            Guid orgId, IMediator mediator,
            [FromQuery] string? status = null,
            [FromQuery] DateTimeOffset? fromDate = null,
            [FromQuery] DateTimeOffset? toDate = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var result = await mediator.Send(new GetDecisionsQuery(
                orgId, status, fromDate, toDate, page, pageSize));
            return Results.Ok(result);
        });

        // GET /{id} — Detay
        group.MapGet("/{id:guid}", async (Guid orgId, Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetDecisionDetailQuery(orgId, id));
            return Results.Ok(result);
        });

        // POST / — Olustur
        group.MapPost("/", async (Guid orgId, [FromBody] CreateDecisionRequest req, IMediator mediator) =>
        {
            var result = await mediator.Send(new CreateDecisionCommand(
                orgId, req.Title, req.Description, req.AgendaItemId, req.PollId));
            return Results.Created($"/api/v1/organizations/{orgId}/decisions/{result.Id}", result);
        });

        // PUT /{id}/status — Durum guncelle
        group.MapPut("/{id:guid}/status", async (Guid orgId, Guid id, [FromBody] UpdateDecisionStatusRequest req, IMediator mediator) =>
        {
            await mediator.Send(new UpdateDecisionStatusCommand(orgId, id, req.Status));
            return Results.NoContent();
        });

        return app;
    }

    public record CreateDecisionRequest(string Title, string? Description, Guid? AgendaItemId, Guid? PollId);
    public record UpdateDecisionStatusRequest(string Status);
}
