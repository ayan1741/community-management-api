using CommunityManagement.Application.Announcements.Commands;
using CommunityManagement.Application.Announcements.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommunityManagement.Api.Endpoints;

public static class AnnouncementEndpoints
{
    public static IEndpointRouteBuilder MapAnnouncementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{orgId:guid}/announcements")
            .RequireAuthorization();

        // GET / — Duyuru listele
        group.MapGet("/", async (
            Guid orgId, IMediator mediator,
            [FromQuery] string? category = null,
            [FromQuery] string? priority = null,
            [FromQuery] string? status = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var result = await mediator.Send(new GetAnnouncementsQuery(orgId, category, priority, status, page, pageSize));
            return Results.Ok(result);
        });

        // POST / — Duyuru oluştur (taslak)
        group.MapPost("/", async (Guid orgId, [FromBody] CreateAnnouncementRequest req, IMediator mediator) =>
        {
            var result = await mediator.Send(new CreateAnnouncementCommand(
                orgId, req.Title, req.Body, req.Category, req.Priority,
                req.TargetType, req.TargetIds, req.ExpiresAt));
            return Results.Created($"/api/v1/organizations/{orgId}/announcements/{result.Id}", result);
        });

        // GET /{id} — Duyuru detay + otomatik okundu
        group.MapGet("/{id:guid}", async (Guid orgId, Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetAnnouncementDetailQuery(orgId, id));
            return Results.Ok(result);
        });

        // PUT /{id} — Duyuru düzenle (sadece taslak)
        group.MapPut("/{id:guid}", async (Guid orgId, Guid id, [FromBody] UpdateAnnouncementRequest req, IMediator mediator) =>
        {
            var result = await mediator.Send(new UpdateAnnouncementCommand(
                orgId, id, req.Title, req.Body, req.Category, req.Priority,
                req.TargetType, req.TargetIds, req.ExpiresAt));
            return Results.Ok(result);
        });

        // DELETE /{id} — Soft delete (Admin only)
        group.MapDelete("/{id:guid}", async (Guid orgId, Guid id, IMediator mediator) =>
        {
            await mediator.Send(new DeleteAnnouncementCommand(orgId, id));
            return Results.NoContent();
        });

        // POST /{id}/publish — Yayınla + bildirim + email job
        group.MapPost("/{id:guid}/publish", async (Guid orgId, Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new PublishAnnouncementCommand(orgId, id));
            return Results.Ok(result);
        });

        // PATCH /{id}/pin — Pinle/unpinle
        group.MapPatch("/{id:guid}/pin", async (Guid orgId, Guid id, [FromBody] PinRequest req, IMediator mediator) =>
        {
            await mediator.Send(new PinAnnouncementCommand(orgId, id, req.IsPinned));
            return Results.NoContent();
        });

        // GET /{id}/reads — Okunma istatistikleri
        group.MapGet("/{id:guid}/reads", async (
            Guid orgId, Guid id, IMediator mediator,
            [FromQuery] string tab = "readers",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var result = await mediator.Send(new GetAnnouncementReadsQuery(orgId, id, tab, page, pageSize));
            return Results.Ok(result);
        });

        // POST /{id}/attachments — Dosya yükle
        group.MapPost("/{id:guid}/attachments", async (
            Guid orgId, Guid id, IMediator mediator, HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault()
                ?? throw Application.Common.AppException.UnprocessableEntity("Dosya bulunamadı.");

            using var stream = file.OpenReadStream();
            var result = await mediator.Send(new UploadAnnouncementAttachmentCommand(
                orgId, id, stream, file.FileName, file.ContentType, file.Length));
            return Results.Ok(result);
        }).DisableAntiforgery();

        return app;
    }

    public record CreateAnnouncementRequest(
        string Title, string Body, string Category, string Priority,
        string TargetType, List<string>? TargetIds, DateTimeOffset? ExpiresAt);

    public record UpdateAnnouncementRequest(
        string Title, string Body, string Category, string Priority,
        string TargetType, List<string>? TargetIds, DateTimeOffset? ExpiresAt);

    public record PinRequest(bool IsPinned);
}
