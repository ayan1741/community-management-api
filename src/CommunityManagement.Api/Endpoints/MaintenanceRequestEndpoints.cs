using CommunityManagement.Application.MaintenanceRequests.Commands;
using CommunityManagement.Application.MaintenanceRequests.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class MaintenanceRequestEndpoints
{
    public static IEndpointRouteBuilder MapMaintenanceRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{orgId:guid}/maintenance-requests")
            .RequireAuthorization();

        // POST / — Ariza bildirimi olustur
        group.MapPost("/", async (Guid orgId, [FromBody] CreateRequest req, IMediator mediator) =>
        {
            var result = await mediator.Send(new CreateMaintenanceRequestCommand(
                orgId, req.Title, req.Description,
                req.Category, req.Priority,
                req.LocationType, req.UnitId, req.LocationNote));
            return Results.Created(
                $"/api/v1/organizations/{orgId}/maintenance-requests/{result.Id}", result);
        });

        // GET / — Ariza listele
        group.MapGet("/", async (
            Guid orgId, IMediator mediator,
            [FromQuery] string? status = null,
            [FromQuery] string? category = null,
            [FromQuery] string? priority = null,
            [FromQuery] string? locationType = null,
            [FromQuery] bool? isRecurring = null,
            [FromQuery] bool? slaBreached = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var result = await mediator.Send(new GetMaintenanceRequestsQuery(
                orgId, status, category, priority, locationType,
                isRecurring, slaBreached, page, pageSize));
            return Results.Ok(result);
        });

        // GET /{id} — Ariza detay (timeline + yorumlar + maliyet)
        group.MapGet("/{id:guid}", async (Guid orgId, Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetMaintenanceRequestDetailQuery(orgId, id));
            return Results.Ok(result);
        });

        // PATCH /{id}/status — Durum guncelle
        group.MapPatch("/{id:guid}/status", async (
            Guid orgId, Guid id, [FromBody] UpdateStatusRequest req, IMediator mediator) =>
        {
            await mediator.Send(new UpdateMaintenanceRequestStatusCommand(orgId, id, req.Status, req.Note));
            return Results.NoContent();
        });

        // PATCH /{id}/assignee — Usta/firma ata veya kaldir
        group.MapPatch("/{id:guid}/assignee", async (
            Guid orgId, Guid id, [FromBody] UpdateAssigneeRequest req, IMediator mediator) =>
        {
            await mediator.Send(new UpdateMaintenanceRequestAssigneeCommand(
                orgId, id, req.Name, req.Phone, req.Note));
            return Results.NoContent();
        });

        // POST /{id}/comments — Yorum ekle (metin + opsiyonel fotograf)
        group.MapPost("/{id:guid}/comments", async (
            Guid orgId, Guid id, IMediator mediator, HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            var content = form["content"].ToString();
            Stream? photoStream = null;
            string? photoFileName = null;
            string? photoContentType = null;
            long photoLength = 0;

            var file = form.Files.GetFile("photo");
            if (file is not null)
            {
                photoStream = file.OpenReadStream();
                photoFileName = file.FileName;
                photoContentType = file.ContentType;
                photoLength = file.Length;
            }

            try
            {
                var result = await mediator.Send(new AddMaintenanceRequestCommentCommand(
                    orgId, id, content, photoStream, photoFileName, photoContentType, photoLength));
                return Results.Created(
                    $"/api/v1/organizations/{orgId}/maintenance-requests/{id}/comments/{result.Id}", result);
            }
            finally
            {
                photoStream?.Dispose();
            }
        }).DisableAntiforgery();

        // POST /{id}/costs — Maliyet kaydi ekle
        group.MapPost("/{id:guid}/costs", async (
            Guid orgId, Guid id, [FromBody] AddCostRequest req, IMediator mediator) =>
        {
            var result = await mediator.Send(new AddMaintenanceRequestCostCommand(
                orgId, id, req.Amount, req.Description));
            return Results.Created(
                $"/api/v1/organizations/{orgId}/maintenance-requests/{id}/costs/{result.Id}", result);
        });

        // POST /{id}/costs/{costId}/transfer — Maliyeti gelir-gidere aktar
        group.MapPost("/{id:guid}/costs/{costId:guid}/transfer", async (
            Guid orgId, Guid id, Guid costId, IMediator mediator) =>
        {
            var result = await mediator.Send(new TransferCostToFinanceCommand(orgId, id, costId));
            return Results.Ok(new { FinanceRecordId = result });
        });

        // POST /{id}/rate — Memnuniyet puani ver
        group.MapPost("/{id:guid}/rate", async (
            Guid orgId, Guid id, [FromBody] RateRequest req, IMediator mediator) =>
        {
            await mediator.Send(new RateMaintenanceRequestCommand(
                orgId, id, req.Rating, req.Comment));
            return Results.NoContent();
        });

        // POST /{id}/photos — Fotograf yukle
        group.MapPost("/{id:guid}/photos", async (
            Guid orgId, Guid id, IMediator mediator, HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault()
                ?? throw Application.Common.AppException.UnprocessableEntity("Dosya bulunamadi.");

            using var stream = file.OpenReadStream();
            var result = await mediator.Send(new UploadMaintenanceRequestPhotoCommand(
                orgId, id, stream, file.FileName, file.ContentType, file.Length));
            return Results.Ok(new { Url = result });
        }).DisableAntiforgery();

        // DELETE /{id} — Soft delete
        group.MapDelete("/{id:guid}", async (Guid orgId, Guid id, IMediator mediator) =>
        {
            await mediator.Send(new DeleteMaintenanceRequestCommand(orgId, id));
            return Results.NoContent();
        });

        // GET /stats — Istatistikler
        group.MapGet("/stats", async (Guid orgId, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetMaintenanceRequestStatsQuery(orgId));
            return Results.Ok(result);
        });

        return app;
    }

    // Request DTO'lari
    public record CreateRequest(
        string Title, string Description,
        string Category, string Priority,
        string LocationType, Guid? UnitId, string? LocationNote);

    public record UpdateStatusRequest(string Status, string? Note);

    public record UpdateAssigneeRequest(string? Name, string? Phone, string? Note);

    public record AddCostRequest(decimal Amount, string? Description);

    public record RateRequest(short Rating, string? Comment);
}
