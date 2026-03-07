using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.MaintenanceRequests.Commands;

public record UpdateMaintenanceRequestAssigneeCommand(
    Guid OrgId, Guid Id,
    string? Name, string? Phone, string? Note
) : IRequest;

public class UpdateMaintenanceRequestAssigneeCommandHandler : IRequestHandler<UpdateMaintenanceRequestAssigneeCommand>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IMaintenanceRequestRepository _repo;

    public UpdateMaintenanceRequestAssigneeCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IMaintenanceRequestRepository repo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
    }

    public async Task Handle(UpdateMaintenanceRequestAssigneeCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.BoardMember, ct);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw AppException.NotFound("Ariza bildirimi bulunamadi.");
        if (entity.OrganizationId != request.OrgId)
            throw AppException.NotFound("Ariza bildirimi bulunamadi.");

        var currentUserId = _currentUser.UserId;
        var now = DateTimeOffset.UtcNow;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                // Atama yap
                await conn.ExecuteAsync(
                    """
                    UPDATE public.maintenance_requests
                    SET assignee_name = @Name, assignee_phone = @Phone, assignee_note = @Note,
                        assigned_at = @Now, updated_by = @UserId, updated_at = @Now
                    WHERE id = @Id
                    """,
                    new { request.Name, request.Phone, request.Note, Now = now.UtcDateTime, UserId = currentUserId, request.Id }, tx);

                // Otomatik durum gecisi: reported/in_review → assigned
                if (entity.Status is "reported" or "in_review")
                {
                    await conn.ExecuteAsync(
                        "UPDATE public.maintenance_requests SET status = 'assigned' WHERE id = @Id",
                        new { request.Id }, tx);

                    await conn.ExecuteAsync(
                        """
                        INSERT INTO public.maintenance_request_logs
                            (id, maintenance_request_id, from_status, to_status, note, created_by, created_at)
                        VALUES (@LogId, @MrId, @FromStatus, 'assigned', @Note, @UserId, @Now)
                        """,
                        new
                        {
                            LogId = Guid.NewGuid(), MrId = request.Id,
                            FromStatus = entity.Status,
                            Note = $"Usta/firma atandi: {request.Name}",
                            UserId = currentUserId, Now = now.UtcDateTime
                        }, tx);
                }

                // Bildiren sakine bildirim
                await conn.ExecuteAsync(
                    """
                    INSERT INTO public.notifications
                        (id, organization_id, user_id, type, title, body, reference_type, reference_id, created_at)
                    VALUES (@Id, @OrgId, @UserId, 'maintenance_assigned', @Title, @Body,
                            'maintenance_request', @RefId, @Now)
                    """,
                    new
                    {
                        Id = Guid.NewGuid(), OrgId = request.OrgId, UserId = entity.ReportedBy,
                        Title = $"Ariza Atandi: {request.Name}",
                        Body = $"{entity.Title} — {request.Name}",
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
                            NewStatus = "assigned",
                            Note = $"Usta/firma: {request.Name}"
                        }),
                        Now = now.UtcDateTime
                    }, tx);
            }
            else
            {
                // Atama kaldir
                await conn.ExecuteAsync(
                    """
                    UPDATE public.maintenance_requests
                    SET assignee_name = NULL, assignee_phone = NULL, assignee_note = NULL,
                        assigned_at = NULL, updated_by = @UserId, updated_at = @Now
                    WHERE id = @Id
                    """,
                    new { UserId = currentUserId, Now = now.UtcDateTime, request.Id }, tx);
            }

            // Audit log
            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (organization_id, table_name, record_id, actor_id, action, new_values)
                VALUES (@OrgId, 'maintenance_requests', @RecordId, @ActorId, 'update', @NewValues::jsonb)
                """,
                new
                {
                    OrgId = request.OrgId, RecordId = request.Id, ActorId = currentUserId,
                    NewValues = JsonSerializer.Serialize(new
                    {
                        AssigneeName = request.Name, AssigneePhone = request.Phone
                    })
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
