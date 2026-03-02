using CommunityManagement.Application.UnitResidents.Commands;
using CommunityManagement.Application.UnitResidents.Queries;
using CommunityManagement.Core.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class UnitResidentEndpoints
{
    public static IEndpointRouteBuilder MapUnitResidentEndpoints(this IEndpointRouteBuilder app)
    {
        var unitGroup = app.MapGroup("/api/v1/organizations/{orgId:guid}/units/{unitId:guid}/residents")
            .RequireAuthorization();

        unitGroup.MapGet("/", async (
            Guid orgId,
            Guid unitId,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new GetUnitResidentsQuery(orgId, unitId));
            return Results.Ok(result);
        });

        unitGroup.MapPost("/", async (
            Guid orgId,
            Guid unitId,
            [FromBody] AssignUnitResidentRequest req,
            IMediator mediator) =>
        {
            var residentType = Enum.TryParse<ResidentType>(req.ResidentType, true, out var rt) ? rt : ResidentType.Unspecified;
            var result = await mediator.Send(new AssignUnitResidentCommand(orgId, unitId, req.UserId, residentType));
            return Results.Created($"/api/v1/organizations/{orgId}/units/{unitId}/residents/{result.Id}", result);
        });

        // Remove endpoint uses a different route pattern
        var removeGroup = app.MapGroup("/api/v1/organizations/{orgId:guid}/unit-residents")
            .RequireAuthorization();

        removeGroup.MapDelete("/{id:guid}", async (
            Guid orgId,
            Guid id,
            IMediator mediator) =>
        {
            await mediator.Send(new RemoveUnitResidentCommand(orgId, id));
            return Results.NoContent();
        });

        return app;
    }

    public record AssignUnitResidentRequest(Guid UserId, string ResidentType);
}
