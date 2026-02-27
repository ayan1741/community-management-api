using CommunityManagement.Application.Members.Commands;
using CommunityManagement.Application.Members.Queries;
using CommunityManagement.Core.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class MemberEndpoints
{
    public static IEndpointRouteBuilder MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{orgId:guid}/members").RequireAuthorization();

        group.MapGet("/", async (
            Guid orgId,
            [FromQuery] string? status,
            [FromQuery] string? role,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            IMediator mediator) =>
        {
            var statusEnum = status is not null && Enum.TryParse<MemberStatus>(status, true, out var s) ? s : (MemberStatus?)null;
            var roleEnum = role is not null && Enum.TryParse<MemberRole>(role, true, out var r) ? r : (MemberRole?)null;
            var result = await mediator.Send(new GetMembersQuery(orgId, statusEnum, roleEnum, page, pageSize));
            return Results.Ok(result);
        });

        group.MapPatch("/{userId:guid}/role", async (
            Guid orgId,
            Guid userId,
            [FromBody] ChangeRoleRequest req,
            IMediator mediator) =>
        {
            if (!Enum.TryParse<MemberRole>(req.Role, true, out var role))
                return Results.BadRequest(new { error = "Geçersiz rol." });

            await mediator.Send(new ChangeMemberRoleCommand(orgId, userId, role));
            return Results.Ok(new { message = "Rol güncellendi." });
        });

        group.MapPost("/{userId:guid}/suspend", async (
            Guid orgId,
            Guid userId,
            IMediator mediator) =>
        {
            await mediator.Send(new SuspendMemberCommand(orgId, userId));
            return Results.Ok(new { message = "Üye askıya alındı." });
        });

        group.MapPost("/{userId:guid}/activate", async (
            Guid orgId,
            Guid userId,
            IMediator mediator) =>
        {
            await mediator.Send(new ActivateMemberCommand(orgId, userId));
            return Results.Ok(new { message = "Üye yeniden aktive edildi." });
        });

        group.MapDelete("/{userId:guid}", async (
            Guid orgId,
            Guid userId,
            IMediator mediator) =>
        {
            await mediator.Send(new RemoveMemberCommand(orgId, userId));
            return Results.Ok(new { message = "Üye organizasyondan çıkarıldı." });
        });

        group.MapGet("/{userId:guid}/history", async (
            Guid orgId,
            Guid userId,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new GetMemberHistoryQuery(orgId, userId));
            return Results.Ok(result);
        });

        return app;
    }

    public record ChangeRoleRequest(string Role);
}
