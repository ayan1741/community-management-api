using CommunityManagement.Application.Finance.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class FinanceReportEndpoints
{
    public static IEndpointRouteBuilder MapFinanceReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{orgId:guid}/finance/reports")
            .RequireAuthorization();

        group.MapGet("/monthly", async (Guid orgId, IMediator mediator,
            [FromQuery] int year = 0, [FromQuery] int month = 0,
            [FromQuery] string reportBasis = "period") =>
        {
            if (year == 0) year = DateTime.UtcNow.Year;
            if (month == 0) month = DateTime.UtcNow.Month;
            var result = await mediator.Send(new GetMonthlyReportQuery(orgId, year, month, reportBasis));
            return Results.Ok(result);
        });

        group.MapGet("/annual", async (Guid orgId, IMediator mediator,
            [FromQuery] int year = 0,
            [FromQuery] string reportBasis = "period") =>
        {
            if (year == 0) year = DateTime.UtcNow.Year;
            var result = await mediator.Send(new GetAnnualReportQuery(orgId, year, reportBasis));
            return Results.Ok(result);
        });

        group.MapGet("/budget-comparison", async (Guid orgId, IMediator mediator,
            [FromQuery] int year = 0, [FromQuery] int? month = null,
            [FromQuery] string reportBasis = "period") =>
        {
            if (year == 0) year = DateTime.UtcNow.Year;
            var result = await mediator.Send(new GetBudgetVsActualQuery(orgId, year, month, reportBasis));
            return Results.Ok(result);
        });

        group.MapGet("/resident-summary", async (Guid orgId, IMediator mediator,
            [FromQuery] int year = 0, [FromQuery] int month = 0,
            [FromQuery] string reportBasis = "period") =>
        {
            if (year == 0) year = DateTime.UtcNow.Year;
            if (month == 0) month = DateTime.UtcNow.Month;
            var result = await mediator.Send(new GetResidentFinanceSummaryQuery(orgId, year, month, reportBasis));
            return Results.Ok(result);
        });

        return app;
    }
}
