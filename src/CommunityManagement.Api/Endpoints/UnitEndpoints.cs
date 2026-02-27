using CommunityManagement.Application.Units.Commands;
using CommunityManagement.Application.Units.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class UnitEndpoints
{
    public static IEndpointRouteBuilder MapUnitEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{orgId:guid}/units").RequireAuthorization();

        group.MapGet("/", async (
            Guid orgId,
            IMediator mediator,
            [FromQuery] Guid? blockId,
            [FromQuery] string? unitType,
            [FromQuery] bool? isOccupied,
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var result = await mediator.Send(new GetUnitsQuery(
                orgId, blockId, unitType, isOccupied, search, page, pageSize));
            return Results.Ok(result);
        });

        group.MapGet("/dropdown", async (
            Guid orgId,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new GetUnitDropdownQuery(orgId));
            return Results.Ok(result);
        });

        group.MapPost("/", async (
            Guid orgId,
            [FromBody] CreateUnitRequest req,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new CreateUnitCommand(
                orgId, req.BlockId, req.UnitNumber, req.UnitType, req.Floor, req.AreaSqm, req.Notes));
            return Results.Created($"/api/v1/organizations/{orgId}/units/{result.Id}", result);
        });

        group.MapPost("/bulk", async (
            Guid orgId,
            [FromBody] BulkCreateRequest req,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new CreateUnitsBulkCommand(
                orgId, req.BlockId, req.StartFloor, req.EndFloor, req.UnitsPerFloor, req.NumberFormat));
            return Results.Ok(result);
        });

        group.MapPut("/{unitId:guid}", async (
            Guid orgId,
            Guid unitId,
            [FromBody] UpdateUnitRequest req,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new UpdateUnitCommand(
                orgId, unitId, req.UnitNumber, req.UnitType, req.Floor, req.AreaSqm, req.Notes));
            return Results.Ok(result);
        });

        group.MapDelete("/{unitId:guid}", async (
            Guid orgId,
            Guid unitId,
            IMediator mediator) =>
        {
            await mediator.Send(new DeleteUnitCommand(orgId, unitId));
            return Results.NoContent();
        });

        return app;
    }

    public record CreateUnitRequest(
        Guid BlockId,
        string UnitNumber,
        string UnitType,
        int? Floor,
        decimal? AreaSqm,
        string? Notes
    );

    public record UpdateUnitRequest(
        string UnitNumber,
        string UnitType,
        int? Floor,
        decimal? AreaSqm,
        string? Notes
    );

    public record BulkCreateRequest(
        Guid BlockId,
        int StartFloor,
        int EndFloor,
        int UnitsPerFloor,
        string NumberFormat
    );
}
