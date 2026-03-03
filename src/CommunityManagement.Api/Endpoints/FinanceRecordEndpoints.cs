using CommunityManagement.Application.Finance.Commands;
using CommunityManagement.Application.Finance.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class FinanceRecordEndpoints
{
    public static IEndpointRouteBuilder MapFinanceRecordEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{orgId:guid}/finance/records")
            .RequireAuthorization();

        group.MapGet("/", async (
            Guid orgId, IMediator mediator,
            [FromQuery] string? type = null,
            [FromQuery] Guid? categoryId = null,
            [FromQuery] DateOnly? startDate = null,
            [FromQuery] DateOnly? endDate = null,
            [FromQuery] int? periodYear = null,
            [FromQuery] int? periodMonth = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var result = await mediator.Send(new GetFinanceRecordsQuery(
                orgId, type, categoryId, startDate, endDate, periodYear, periodMonth, page, pageSize));
            return Results.Ok(result);
        });

        group.MapPost("/", async (Guid orgId, [FromBody] CreateRecordRequest req, IMediator mediator) =>
        {
            var result = await mediator.Send(new CreateFinanceRecordCommand(
                orgId, req.CategoryId, req.Type, req.Amount, req.RecordDate, req.Description, req.PaymentMethod, req.PeriodYear, req.PeriodMonth));
            return Results.Created($"/api/v1/organizations/{orgId}/finance/records/{result.Id}", result);
        });

        group.MapPut("/{recordId:guid}", async (Guid orgId, Guid recordId,
            [FromBody] UpdateRecordRequest req, IMediator mediator) =>
        {
            var result = await mediator.Send(new UpdateFinanceRecordCommand(
                orgId, recordId, req.CategoryId, req.Amount, req.RecordDate, req.Description, req.PaymentMethod, req.PeriodYear, req.PeriodMonth));
            return Results.Ok(result);
        });

        group.MapDelete("/{recordId:guid}", async (Guid orgId, Guid recordId, IMediator mediator) =>
        {
            await mediator.Send(new SoftDeleteFinanceRecordCommand(orgId, recordId));
            return Results.NoContent();
        });

        group.MapPost("/{recordId:guid}/document", async (
            Guid orgId, Guid recordId, IMediator mediator, HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault()
                ?? throw Application.Common.AppException.UnprocessableEntity("Dosya bulunamadı.");

            using var stream = file.OpenReadStream();
            var url = await mediator.Send(new UploadFinanceDocumentCommand(
                orgId, recordId, stream, file.FileName, file.ContentType, file.Length));

            return Results.Ok(new { documentUrl = url });
        }).DisableAntiforgery();

        return app;
    }

    public record CreateRecordRequest(Guid CategoryId, string Type, decimal Amount, DateOnly RecordDate, string Description, string? PaymentMethod, int? PeriodYear, int? PeriodMonth);
    public record UpdateRecordRequest(Guid CategoryId, decimal Amount, DateOnly RecordDate, string Description, string? PaymentMethod, int? PeriodYear, int? PeriodMonth);
}
