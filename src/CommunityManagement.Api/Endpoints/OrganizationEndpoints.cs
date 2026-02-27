using CommunityManagement.Application.Organizations.Commands;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations").RequireAuthorization();

        group.MapPost("/", async (
            [FromBody] CreateOrganizationRequest req,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new CreateOrganizationCommand(
                req.Name, req.OrgType, req.AddressDistrict, req.AddressCity, req.ContactPhone));
            return Results.Created($"/api/v1/organizations/{result.OrganizationId}", result);
        });

        return app;
    }

    public record CreateOrganizationRequest(
        string Name,
        string OrgType,
        string? AddressDistrict,
        string? AddressCity,
        string? ContactPhone
    );
}
