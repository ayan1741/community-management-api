using CommunityManagement.Application.Dues.Commands;
using CommunityManagement.Application.Dues.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class DuesSummaryEndpoints
{
    public static IEndpointRouteBuilder MapDuesSummaryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{orgId:guid}").RequireAuthorization();

        group.MapGet("/dues-summary", async (
            Guid orgId,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new GetDuesSummaryQuery(orgId));
            return Results.Ok(result);
        });

        group.MapGet("/due-settings", async (
            Guid orgId,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new GetDueSettingsQuery(orgId));
            return Results.Ok(result);
        });

        group.MapPut("/due-settings", async (
            Guid orgId,
            [FromBody] UpdateDueSettingsRequest req,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new UpdateDueSettingsCommand(
                orgId, req.LateFeeRate, req.LateFeeGraceDays, req.ReminderDaysBefore));
            return Results.Ok(result);
        });

        group.MapGet("/my-dues", async (
            Guid orgId,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new GetMyDuesQuery(orgId));
            return Results.Ok(result);
        });

        group.MapGet("/my-payments", async (
            Guid orgId,
            IMediator mediator,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var result = await mediator.Send(new GetMyPaymentHistoryQuery(orgId, page, pageSize));
            return Results.Ok(result);
        });

        return app;
    }

    public record UpdateDueSettingsRequest(
        decimal LateFeeRate,
        int LateFeeGraceDays,
        int ReminderDaysBefore
    );
}
