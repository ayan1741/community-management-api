using CommunityManagement.Application.Blocks.Commands;
using CommunityManagement.Application.Blocks.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class BlockEndpoints
{
    public static IEndpointRouteBuilder MapBlockEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{orgId:guid}/blocks").RequireAuthorization();

        group.MapGet("/", async (
            Guid orgId,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new GetBlocksQuery(orgId));
            return Results.Ok(result);
        });

        group.MapPost("/", async (
            Guid orgId,
            [FromBody] CreateBlockRequest req,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new CreateBlockCommand(orgId, req.Name));
            return Results.Created($"/api/v1/organizations/{orgId}/blocks/{result.Id}", result);
        });

        group.MapPut("/{blockId:guid}", async (
            Guid orgId,
            Guid blockId,
            [FromBody] UpdateBlockRequest req,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new UpdateBlockCommand(orgId, blockId, req.Name));
            return Results.Ok(result);
        });

        group.MapDelete("/{blockId:guid}", async (
            Guid orgId,
            Guid blockId,
            IMediator mediator) =>
        {
            await mediator.Send(new DeleteBlockCommand(orgId, blockId));
            return Results.NoContent();
        });

        return app;
    }

    public record CreateBlockRequest(string Name);
    public record UpdateBlockRequest(string Name);
}
