using CommunityManagement.Application.Dues.Commands;
using CommunityManagement.Application.Dues.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class AccrualEndpoints
{
    public static IEndpointRouteBuilder MapAccrualEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{orgId:guid}/dues-periods/{periodId:guid}").RequireAuthorization();

        group.MapPost("/accrue", async (
            Guid orgId,
            Guid periodId,
            [FromBody] AccrualRequest req,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new TriggerBulkAccrualCommand(
                orgId, periodId, req.DueTypeIds, req.IncludeEmptyUnits, req.Confirmed));

            if (result.JobId is null)
                return Results.Ok(result.Preview);

            return Results.Accepted(
                $"/api/v1/organizations/{orgId}/dues-periods/{periodId}",
                new { jobId = result.JobId, preview = result.Preview });
        });

        group.MapPost("/unit-dues", async (
            Guid orgId,
            Guid periodId,
            [FromBody] ManualUnitDueRequest req,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new CreateManualUnitDueCommand(
                orgId, periodId, req.UnitId, req.DueTypeId, req.Amount, req.Note));
            return Results.Created(
                $"/api/v1/organizations/{orgId}/dues-periods/{periodId}/unit-dues/{result.Id}",
                result);
        });

        group.MapDelete("/unit-dues/{unitDueId:guid}", async (
            Guid orgId,
            Guid periodId,
            Guid unitDueId,
            IMediator mediator,
            [FromQuery] bool confirm = false) =>
        {
            await mediator.Send(new CancelUnitDueCommand(orgId, periodId, unitDueId, confirm));
            return Results.NoContent();
        });

        return app;
    }

    public record AccrualRequest(
        IReadOnlyList<Guid> DueTypeIds,
        bool IncludeEmptyUnits,
        bool Confirmed
    );

    public record ManualUnitDueRequest(
        Guid UnitId,
        Guid DueTypeId,
        decimal Amount,
        string? Note
    );
}
