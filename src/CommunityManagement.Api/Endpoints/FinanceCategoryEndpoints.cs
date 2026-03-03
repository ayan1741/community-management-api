using CommunityManagement.Application.Finance.Commands;
using CommunityManagement.Application.Finance.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class FinanceCategoryEndpoints
{
    public static IEndpointRouteBuilder MapFinanceCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{orgId:guid}/finance/categories")
            .RequireAuthorization();

        group.MapGet("/", async (Guid orgId, IMediator mediator,
            [FromQuery] string? type = null, [FromQuery] bool? isActive = null) =>
        {
            var result = await mediator.Send(new GetFinanceCategoriesQuery(orgId, type, isActive));
            return Results.Ok(result);
        });

        group.MapPost("/", async (Guid orgId, [FromBody] CreateCategoryRequest req, IMediator mediator) =>
        {
            var result = await mediator.Send(new CreateFinanceCategoryCommand(
                orgId, req.Name, req.Type, req.ParentId, req.Icon, req.SortOrder));
            return Results.Created($"/api/v1/organizations/{orgId}/finance/categories/{result.Id}", result);
        });

        group.MapPost("/seed", async (Guid orgId, IMediator mediator) =>
        {
            await mediator.Send(new SeedDefaultFinanceCategoriesCommand(orgId));
            return Results.Ok(new { message = "Varsayılan kategoriler oluşturuldu." });
        });

        group.MapPut("/{categoryId:guid}", async (Guid orgId, Guid categoryId,
            [FromBody] UpdateCategoryRequest req, IMediator mediator) =>
        {
            var result = await mediator.Send(new UpdateFinanceCategoryCommand(
                orgId, categoryId, req.Name, req.Icon, req.SortOrder));
            return Results.Ok(result);
        });

        group.MapPatch("/{categoryId:guid}/toggle", async (Guid orgId, Guid categoryId, IMediator mediator) =>
        {
            await mediator.Send(new ToggleCategoryActiveCommand(orgId, categoryId));
            return Results.Ok(new { message = "Kategori durumu güncellendi." });
        });

        return app;
    }

    public record CreateCategoryRequest(string Name, string Type, Guid? ParentId, string? Icon, int SortOrder = 0);
    public record UpdateCategoryRequest(string Name, string? Icon, int SortOrder = 0);
}
