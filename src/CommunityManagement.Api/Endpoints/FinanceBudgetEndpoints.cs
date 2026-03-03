using CommunityManagement.Application.Finance.Commands;
using CommunityManagement.Application.Finance.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class FinanceBudgetEndpoints
{
    public static IEndpointRouteBuilder MapFinanceBudgetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{orgId:guid}/finance/budgets")
            .RequireAuthorization();

        group.MapGet("/", async (Guid orgId, IMediator mediator,
            [FromQuery] int year = 0, [FromQuery] int? month = null) =>
        {
            if (year == 0) year = DateTime.UtcNow.Year;
            var result = await mediator.Send(new GetBudgetsQuery(orgId, year, month));
            return Results.Ok(result);
        });

        group.MapPut("/", async (Guid orgId, [FromBody] SetBudgetRequest req, IMediator mediator) =>
        {
            var result = await mediator.Send(new SetBudgetCommand(
                orgId, req.CategoryId, req.Year, req.Month, req.Amount));
            return Results.Ok(result);
        });

        group.MapPost("/copy", async (Guid orgId, [FromBody] CopyBudgetRequest req, IMediator mediator) =>
        {
            var count = await mediator.Send(new CopyBudgetCommand(
                orgId, req.FromYear, req.FromMonth, req.ToYear, req.ToMonth));
            return Results.Ok(new { copiedCount = count });
        });

        return app;
    }

    public record SetBudgetRequest(Guid CategoryId, int Year, int Month, decimal Amount);
    public record CopyBudgetRequest(int FromYear, int FromMonth, int ToYear, int ToMonth);
}
