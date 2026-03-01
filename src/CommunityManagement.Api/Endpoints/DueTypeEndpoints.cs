using CommunityManagement.Application.Dues.Commands;
using CommunityManagement.Application.Dues.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class DueTypeEndpoints
{
    public static IEndpointRouteBuilder MapDueTypeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{orgId:guid}/due-types").RequireAuthorization();

        group.MapGet("/", async (
            Guid orgId,
            IMediator mediator,
            [FromQuery] bool? isActive) =>
        {
            var result = await mediator.Send(new GetDueTypesQuery(orgId, isActive));
            return Results.Ok(result);
        });

        group.MapPost("/", async (
            Guid orgId,
            [FromBody] CreateDueTypeRequest req,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new CreateDueTypeCommand(
                orgId, req.Name, req.Description, req.DefaultAmount, req.CategoryAmounts));
            return Results.Created($"/api/v1/organizations/{orgId}/due-types/{result.Id}", result);
        });

        group.MapPut("/{typeId:guid}", async (
            Guid orgId,
            Guid typeId,
            [FromBody] UpdateDueTypeRequest req,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new UpdateDueTypeCommand(
                orgId, typeId, req.Name, req.Description, req.DefaultAmount, req.CategoryAmounts));
            return Results.Ok(result);
        });

        group.MapPatch("/{typeId:guid}/deactivate", async (
            Guid orgId,
            Guid typeId,
            IMediator mediator) =>
        {
            await mediator.Send(new DeactivateDueTypeCommand(orgId, typeId));
            return Results.Ok();
        });

        return app;
    }

    public record CreateDueTypeRequest(
        string Name,
        string? Description,
        decimal DefaultAmount,
        string? CategoryAmounts
    );

    public record UpdateDueTypeRequest(
        string Name,
        string? Description,
        decimal DefaultAmount,
        string? CategoryAmounts
    );
}
