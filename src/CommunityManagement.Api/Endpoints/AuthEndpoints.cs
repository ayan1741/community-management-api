using CommunityManagement.Application.Auth.Commands;
using CommunityManagement.Application.Auth.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization();

        group.MapGet("/me", async (IMediator mediator) =>
        {
            var result = await mediator.Send(new GetMyContextQuery());
            return Results.Ok(new
            {
                profile = new
                {
                    id = result.Profile.Id,
                    fullName = result.Profile.FullName,
                    phone = result.Profile.Phone,
                    avatarUrl = result.Profile.AvatarUrl,
                    kvkkConsentAt = result.Profile.KvkkConsentAt
                },
                memberships = result.Memberships.Select(m => new
                {
                    organizationId = m.OrganizationId,
                    organizationName = m.OrganizationName,
                    role = m.Role,
                    status = m.Status,
                    units = m.Units.Select(u => new
                    {
                        unitId = u.UnitId,
                        unitNumber = u.UnitNumber,
                        blockName = u.BlockName,
                        residentType = u.ResidentType
                    })
                })
            });
        });

        group.MapPatch("/profile", async (
            [FromBody] UpdateProfileRequest req,
            IMediator mediator) =>
        {
            var profile = await mediator.Send(new UpdateProfileCommand(req.FullName, req.Phone));
            return Results.Ok(new
            {
                id = profile.Id,
                fullName = profile.FullName,
                phone = profile.Phone,
                avatarUrl = profile.AvatarUrl,
                kvkkConsentAt = profile.KvkkConsentAt
            });
        });

        group.MapPost("/profile/change-email", async (
            [FromBody] ChangeEmailRequest req,
            IMediator mediator) =>
        {
            await mediator.Send(new ChangeEmailCommand(req.NewEmail));
            return Results.Ok(new { message = "Yeni e-posta adresine onay linki gönderildi." });
        });

        group.MapPost("/profile/kvkk-consent", async (IMediator mediator) =>
        {
            var consentAt = await mediator.Send(new RecordKvkkConsentCommand());
            return Results.Ok(new { kvkkConsentAt = consentAt });
        });

        group.MapDelete("/profile", async (IMediator mediator) =>
        {
            var scheduledAt = await mediator.Send(new RequestAccountDeletionCommand());
            return Results.Ok(new { scheduledDeletionAt = scheduledAt });
        });

        group.MapPost("/auth/revoke-all-sessions", async (IMediator mediator) =>
        {
            await mediator.Send(new RevokeAllSessionsCommand());
            return Results.Ok(new { message = "Tüm oturumlar kapatıldı." });
        });

        return app;
    }

    public record UpdateProfileRequest(string? FullName, string? Phone);
    public record ChangeEmailRequest(string NewEmail);
}
