using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.MaintenanceRequests.Commands;

public record UpdateMaintenanceRequestStatusCommand(
    Guid OrgId, Guid Id, string Status, string? Note
) : IRequest;

public class UpdateMaintenanceRequestStatusCommandHandler : IRequestHandler<UpdateMaintenanceRequestStatusCommand>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IMaintenanceRequestRepository _repo;

    private static readonly Dictionary<string, HashSet<string>> ValidTransitions = new()
    {
        ["reported"]    = new() { "in_review", "cancelled" },
        ["in_review"]   = new() { "assigned", "reported", "cancelled" },
        ["assigned"]    = new() { "in_progress", "in_review", "cancelled" },
        ["in_progress"] = new() { "resolved", "assigned", "cancelled" },
        ["resolved"]    = new() { "closed", "in_progress" },
    };

    public UpdateMaintenanceRequestStatusCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IMaintenanceRequestRepository repo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
    }

    public async Task Handle(UpdateMaintenanceRequestStatusCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.BoardMember, ct);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw AppException.NotFound("Ariza bildirimi bulunamadi.");
        if (entity.OrganizationId != request.OrgId)
            throw AppException.NotFound("Ariza bildirimi bulunamadi.");

        if (!ValidTransitions.TryGetValue(entity.Status, out var allowed) || !allowed.Contains(request.Status))
            throw AppException.UnprocessableEntity($"'{entity.Status}' durumundan '{request.Status}' durumuna gecis yapilamaz.");

        var currentUserId = _currentUser.UserId;
        var now = DateTimeOffset.UtcNow;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            // Durum + ilgili timestamp guncelle
            var updateSql = request.Status switch
            {
                "resolved" => "UPDATE public.maintenance_requests SET status = @Status, resolved_at = @Now, updated_by = @UserId, updated_at = @Now WHERE id = @Id",
                "closed" => "UPDATE public.maintenance_requests SET status = @Status, closed_at = @Now, updated_by = @UserId, updated_at = @Now WHERE id = @Id",
                "cancelled" => "UPDATE public.maintenance_requests SET status = @Status, cancelled_at = @Now, cancelled_by = @UserId, updated_by = @UserId, updated_at = @Now WHERE id = @Id",
                _ => "UPDATE public.maintenance_requests SET status = @Status, updated_by = @UserId, updated_at = @Now WHERE id = @Id"
            };
            await conn.ExecuteAsync(updateSql, new { request.Status, Now = now.UtcDateTime, UserId = currentUserId, request.Id }, tx);

            // Log kaydi
            await conn.ExecuteAsync(
                """
                INSERT INTO public.maintenance_request_logs
                    (id, maintenance_request_id, from_status, to_status, note, created_by, created_at)
                VALUES (@LogId, @MrId, @FromStatus, @ToStatus, @Note, @UserId, @Now)
                """,
                new
                {
                    LogId = Guid.NewGuid(), MrId = request.Id,
                    FromStatus = entity.Status, ToStatus = request.Status,
                    request.Note, UserId = currentUserId, Now = now.UtcDateTime
                }, tx);

            // Bildiren sakine bildirim
            await conn.ExecuteAsync(
                """
                INSERT INTO public.notifications
                    (id, organization_id, user_id, type, title, body, reference_type, reference_id, created_at)
                VALUES (@Id, @OrgId, @UserId, 'maintenance_status_changed', @Title, @Body,
                        'maintenance_request', @RefId, @Now)
                """,
                new
                {
                    Id = Guid.NewGuid(), OrgId = request.OrgId, UserId = entity.ReportedBy,
                    Title = $"Ariza Durumu: {request.Status}",
                    Body = $"{entity.Title} — {request.Status}",
                    RefId = request.Id, Now = now.UtcDateTime
                }, tx);

            // Email job
            await conn.ExecuteAsync(
                """
                INSERT INTO public.background_jobs (job_type, payload, status, created_at)
                VALUES ('maintenance_status_email', @Payload::jsonb, 'queued', @Now)
                """,
                new
                {
                    Payload = JsonSerializer.Serialize(new
                    {
                        MaintenanceRequestId = request.Id,
                        OrganizationId = request.OrgId,
                        NewStatus = request.Status,
                        request.Note
                    }),
                    Now = now.UtcDateTime
                }, tx);

            // Audit log
            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (organization_id, table_name, record_id, actor_id, action, new_values, old_values)
                VALUES (@OrgId, 'maintenance_requests', @RecordId, @ActorId, 'update', @NewValues::jsonb, @OldValues::jsonb)
                """,
                new
                {
                    OrgId = request.OrgId, RecordId = request.Id, ActorId = currentUserId,
                    NewValues = JsonSerializer.Serialize(new { Status = request.Status }),
                    OldValues = JsonSerializer.Serialize(new { Status = entity.Status })
                }, tx);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
