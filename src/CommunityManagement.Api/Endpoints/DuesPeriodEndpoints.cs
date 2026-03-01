using CommunityManagement.Application.Dues.Commands;
using CommunityManagement.Application.Dues.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class DuesPeriodEndpoints
{
    public static IEndpointRouteBuilder MapDuesPeriodEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{orgId:guid}/dues-periods").RequireAuthorization();

        group.MapGet("/", async (
            Guid orgId,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new GetDuesPeriodsQuery(orgId));
            return Results.Ok(result);
        });

        group.MapGet("/{periodId:guid}", async (
            Guid orgId,
            Guid periodId,
            IMediator mediator,
            [FromQuery] string? status,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var result = await mediator.Send(new GetDuesPeriodDetailQuery(
                orgId, periodId, status, page, pageSize));
            return Results.Ok(result);
        });

        group.MapPost("/", async (
            Guid orgId,
            [FromBody] CreateDuesPeriodRequest req,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new CreateDuesPeriodCommand(
                orgId, req.Name, req.StartDate, req.DueDate));
            return Results.Created($"/api/v1/organizations/{orgId}/dues-periods/{result.Id}", result);
        });

        group.MapDelete("/{periodId:guid}", async (
            Guid orgId,
            Guid periodId,
            IMediator mediator) =>
        {
            await mediator.Send(new DeleteDuesPeriodCommand(orgId, periodId));
            return Results.NoContent();
        });

        group.MapPost("/{periodId:guid}/close", async (
            Guid orgId,
            Guid periodId,
            IMediator mediator) =>
        {
            await mediator.Send(new CloseDuesPeriodCommand(orgId, periodId));
            return Results.Ok();
        });

        return app;
    }

    public record CreateDuesPeriodRequest(
        string Name,
        DateOnly StartDate,
        DateOnly DueDate
    );
}
