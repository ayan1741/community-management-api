using CommunityManagement.Application.Applications.Commands;
using CommunityManagement.Application.Applications.Queries;
using CommunityManagement.Core.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class ApplicationEndpoints
{
    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        var authed = app.MapGroup("/api/v1").RequireAuthorization();

        authed.MapPost("/applications", async (
            [FromBody] SubmitApplicationRequest req,
            IMediator mediator) =>
        {
            var residentType = Enum.TryParse<ResidentType>(req.ResidentType, true, out var rt)
                ? rt : ResidentType.Unspecified;

            var result = await mediator.Send(new SubmitApplicationCommand(
                req.InvitationCode, req.OrganizationId, req.UnitId, residentType));
            return Results.Created($"/api/v1/applications/{result.ApplicationId}", result);
        });

        authed.MapDelete("/applications/{applicationId:guid}", async (
            Guid applicationId,
            IMediator mediator) =>
        {
            await mediator.Send(new WithdrawApplicationCommand(applicationId));
            return Results.Ok(new { message = "Başvurunuz geri çekildi." });
        });

        var orgGroup = app.MapGroup("/api/v1/organizations/{orgId:guid}/applications").RequireAuthorization();

        orgGroup.MapGet("/", async (
            Guid orgId,
            IMediator mediator,
            [FromQuery] string? status,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var statusEnum = status is not null && Enum.TryParse<ApplicationStatus>(status, true, out var s)
                ? s : (ApplicationStatus?)null;
            var result = await mediator.Send(new GetPendingApplicationsQuery(orgId, statusEnum, page, pageSize));
            return Results.Ok(result);
        });

        orgGroup.MapPost("/{applicationId:guid}/approve", async (
            Guid orgId,
            Guid applicationId,
            IMediator mediator) =>
        {
            await mediator.Send(new ApproveApplicationCommand(orgId, applicationId));
            return Results.Ok(new { message = "Başvuru onaylandı." });
        });

        orgGroup.MapPost("/{applicationId:guid}/reject", async (
            Guid orgId,
            Guid applicationId,
            [FromBody] RejectApplicationRequest req,
            IMediator mediator) =>
        {
            await mediator.Send(new RejectApplicationCommand(orgId, applicationId, req.Reason));
            return Results.Ok(new { message = "Başvuru reddedildi." });
        });

        return app;
    }

    public record SubmitApplicationRequest(
        string? InvitationCode,
        Guid? OrganizationId,
        Guid? UnitId,
        string ResidentType
    );

    public record RejectApplicationRequest(string? Reason);
}
