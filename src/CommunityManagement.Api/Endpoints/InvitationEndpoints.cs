using CommunityManagement.Application.Invitations.Commands;
using CommunityManagement.Application.Invitations.Queries;
using CommunityManagement.Core.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class InvitationEndpoints
{
    public static IEndpointRouteBuilder MapInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        // Public — no auth required
        app.MapGet("/api/v1/invitations/validate", async (
            [FromQuery] string code,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new ValidateInvitationCodeQuery(code));
            return Results.Ok(result);
        });

        var group = app.MapGroup("/api/v1/organizations/{orgId:guid}/invitations").RequireAuthorization();

        group.MapGet("/", async (
            Guid orgId,
            IMediator mediator,
            [FromQuery] string? status,
            [FromQuery] Guid? unitId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var statusEnum = status is not null && Enum.TryParse<CodeStatus>(status, true, out var s) ? s : (CodeStatus?)null;
            var result = await mediator.Send(new GetInvitationsQuery(orgId, statusEnum, unitId, page, pageSize));
            return Results.Ok(result);
        });

        group.MapPost("/", async (
            Guid orgId,
            [FromBody] CreateInvitationRequest req,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new CreateInvitationCommand(orgId, req.UnitId, req.ExpiresInDays));
            return Results.Created($"/api/v1/organizations/{orgId}/invitations/{result.InvitationId}", result);
        });

        group.MapDelete("/{invitationId:guid}", async (
            Guid orgId,
            Guid invitationId,
            IMediator mediator) =>
        {
            await mediator.Send(new RevokeInvitationCommand(orgId, invitationId));
            return Results.Ok(new { message = "Davet kodu iptal edildi." });
        });

        return app;
    }

    public record CreateInvitationRequest(Guid UnitId, int ExpiresInDays = 7);
}
