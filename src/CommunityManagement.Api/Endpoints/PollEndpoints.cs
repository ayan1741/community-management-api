using CommunityManagement.Application.Polls.Commands;
using CommunityManagement.Application.Polls.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class PollEndpoints
{
    public static IEndpointRouteBuilder MapPollEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{orgId:guid}/polls")
            .RequireAuthorization();

        // GET / — Liste
        group.MapGet("/", async (
            Guid orgId, IMediator mediator,
            [FromQuery] string? status = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var result = await mediator.Send(new GetPollsQuery(orgId, status, page, pageSize));
            return Results.Ok(result);
        });

        // GET /{id} — Detay + secenekler + kullanicinin oyu
        group.MapGet("/{id:guid}", async (Guid orgId, Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetPollDetailQuery(orgId, id));
            return Results.Ok(result);
        });

        // POST / — Olustur
        group.MapPost("/", async (Guid orgId, [FromBody] CreatePollRequest req, IMediator mediator) =>
        {
            var options = req.Options?.Select(o => new CreatePollOptionDto(o.Label)).ToList();
            var result = await mediator.Send(new CreatePollCommand(
                orgId, req.Title, req.Description, req.PollType,
                req.StartsAt, req.EndsAt, req.AgendaItemId,
                req.ShowInterimResults, options));
            return Results.Created($"/api/v1/organizations/{orgId}/polls/{result.Id}", result);
        });

        // POST /{id}/vote — Oy kullan
        group.MapPost("/{id:guid}/vote", async (Guid orgId, Guid id, [FromBody] CastVoteRequest req, IMediator mediator) =>
        {
            await mediator.Send(new CastVoteCommand(orgId, id, req.PollOptionId));
            return Results.NoContent();
        });

        // PUT /{id}/end — Erken sonlandir
        group.MapPut("/{id:guid}/end", async (Guid orgId, Guid id, IMediator mediator) =>
        {
            await mediator.Send(new EndPollCommand(orgId, id));
            return Results.NoContent();
        });

        // PUT /{id}/extend — Sure uzat
        group.MapPut("/{id:guid}/extend", async (Guid orgId, Guid id, [FromBody] ExtendPollRequest req, IMediator mediator) =>
        {
            await mediator.Send(new ExtendPollCommand(orgId, id, req.NewEndsAt));
            return Results.NoContent();
        });

        // PUT /{id}/cancel — Iptal
        group.MapPut("/{id:guid}/cancel", async (Guid orgId, Guid id, IMediator mediator) =>
        {
            await mediator.Send(new CancelPollCommand(orgId, id));
            return Results.NoContent();
        });

        // GET /{id}/result — Sonuc
        group.MapGet("/{id:guid}/result", async (Guid orgId, Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetPollResultQuery(orgId, id));
            return Results.Ok(result);
        });

        return app;
    }

    public record CreatePollRequest(
        string Title, string? Description, string PollType,
        DateTimeOffset StartsAt, DateTimeOffset EndsAt,
        Guid? AgendaItemId, bool ShowInterimResults,
        List<PollOptionRequest>? Options);
    public record PollOptionRequest(string Label);
    public record CastVoteRequest(Guid PollOptionId);
    public record ExtendPollRequest(DateTimeOffset NewEndsAt);
}
