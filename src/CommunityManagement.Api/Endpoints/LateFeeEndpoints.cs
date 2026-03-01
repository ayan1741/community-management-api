using CommunityManagement.Application.Dues.Commands;
using CommunityManagement.Application.Dues.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class LateFeeEndpoints
{
    public static IEndpointRouteBuilder MapLateFeeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{orgId:guid}/unit-dues/{unitDueId:guid}").RequireAuthorization();

        group.MapGet("/late-fees", async (
            Guid orgId,
            Guid unitDueId,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new GetLateFeesByUnitDueQuery(orgId, unitDueId));
            return Results.Ok(result);
        });

        group.MapPost("/late-fees", async (
            Guid orgId,
            Guid unitDueId,
            [FromBody] ApplyLateFeeRequest req,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new ApplyLateFeeCommand(orgId, unitDueId, req.Rate, req.Note));
            return Results.Created(
                $"/api/v1/organizations/{orgId}/unit-dues/{unitDueId}/late-fees/{result.Id}",
                result);
        });

        group.MapPatch("/late-fees/{lateFeeId:guid}/cancel", async (
            Guid orgId,
            Guid unitDueId,
            Guid lateFeeId,
            [FromBody] CancelLateFeeRequest req,
            IMediator mediator) =>
        {
            await mediator.Send(new CancelLateFeeCommand(orgId, unitDueId, lateFeeId, req.Note));
            return Results.Ok();
        });

        return app;
    }

    public record ApplyLateFeeRequest(decimal Rate, string? Note);
    public record CancelLateFeeRequest(string Note);
}
