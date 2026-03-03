using CommunityManagement.Application.Finance.Commands;
using CommunityManagement.Application.Finance.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class FinanceOpeningBalanceEndpoints
{
    public static IEndpointRouteBuilder MapFinanceOpeningBalanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{orgId:guid}/finance/opening-balance")
            .RequireAuthorization();

        group.MapGet("/", async (Guid orgId, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetOpeningBalanceQuery(orgId));
            return result is null ? Results.Ok(new { exists = false }) : Results.Ok(new { exists = true, data = result });
        });

        group.MapPost("/", async (Guid orgId, [FromBody] OpeningBalanceRequest req, IMediator mediator) =>
        {
            var result = await mediator.Send(new CreateOpeningBalanceCommand(
                orgId, req.Amount, req.RecordDate, req.Description));
            return Results.Created($"/api/v1/organizations/{orgId}/finance/opening-balance", result);
        });

        group.MapPut("/", async (Guid orgId, [FromBody] OpeningBalanceRequest req, IMediator mediator) =>
        {
            var result = await mediator.Send(new UpdateOpeningBalanceCommand(
                orgId, req.Amount, req.RecordDate, req.Description));
            return Results.Ok(result);
        });

        return app;
    }

    public record OpeningBalanceRequest(decimal Amount, DateOnly RecordDate, string? Description);
}
