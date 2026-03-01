using CommunityManagement.Application.Dues.Commands;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var orgGroup = app.MapGroup("/api/v1/organizations/{orgId:guid}").RequireAuthorization();

        orgGroup.MapPost("/unit-dues/{unitDueId:guid}/payments", async (
            Guid orgId,
            Guid unitDueId,
            [FromBody] RecordPaymentRequest req,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new RecordPaymentCommand(
                orgId, unitDueId, req.Amount, req.PaidAt, req.PaymentMethod, req.Note, req.Confirmed));
            return Results.Created(
                $"/api/v1/organizations/{orgId}/payments/{result.Id}",
                result);
        });

        orgGroup.MapPatch("/payments/{paymentId:guid}", async (
            Guid orgId,
            Guid paymentId,
            [FromBody] UpdatePaymentRequest req,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new UpdatePaymentCommand(
                orgId, paymentId, req.ReceiptNumber, req.Amount, req.PaidAt, req.PaymentMethod, req.Note));
            return Results.Ok(result);
        });

        orgGroup.MapDelete("/payments/{paymentId:guid}", async (
            Guid orgId,
            Guid paymentId,
            IMediator mediator) =>
        {
            await mediator.Send(new SoftDeletePaymentCommand(orgId, paymentId));
            return Results.NoContent();
        });

        return app;
    }

    public record RecordPaymentRequest(
        decimal Amount,
        DateTimeOffset PaidAt,
        string PaymentMethod,
        string? Note,
        bool Confirmed
    );

    public record UpdatePaymentRequest(
        string ReceiptNumber,
        decimal Amount,
        DateTimeOffset PaidAt,
        string PaymentMethod,
        string? Note
    );
}
