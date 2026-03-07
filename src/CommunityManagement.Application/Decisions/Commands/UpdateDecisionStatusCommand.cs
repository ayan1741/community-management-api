using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.Decisions.Commands;

public record UpdateDecisionStatusCommand(Guid OrgId, Guid Id, string Status) : IRequest;

public class UpdateDecisionStatusCommandHandler : IRequestHandler<UpdateDecisionStatusCommand>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IDecisionRepository _repo;

    public UpdateDecisionStatusCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IDecisionRepository repo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
    }

    public async Task Handle(UpdateDecisionStatusCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        if (request.Status is not ("karar_alindi" or "uygulamada" or "tamamlandi" or "iptal"))
            throw AppException.UnprocessableEntity("Gecersiz durum.");

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw AppException.NotFound("Karar bulunamadi.");
        if (entity.OrganizationId != request.OrgId)
            throw AppException.NotFound("Karar bulunamadi.");

        var currentUserId = _currentUser.UserId;
        var now = DateTimeOffset.UtcNow;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                "UPDATE public.decisions SET status = @Status, updated_at = @Now WHERE id = @Id",
                new { request.Id, request.Status, Now = now.UtcDateTime }, tx);

            // Tum uyelere bildirim (batch)
            await conn.ExecuteAsync(
                """
                INSERT INTO public.notifications
                    (organization_id, user_id, type, title, body, reference_type, reference_id, created_at)
                SELECT @OrgId, om.user_id, 'decision_updated', @Title, @Body, 'decision', @RefId, @Now
                FROM public.organization_members om
                WHERE om.organization_id = @OrgId AND om.status = 'active'
                """,
                new
                {
                    request.OrgId,
                    Title = $"Karar Guncellendi: {entity.Title}",
                    Body = $"Yeni durum: {request.Status}",
                    RefId = request.Id, Now = now.UtcDateTime
                }, tx);

            // Background job
            await conn.ExecuteAsync(
                """
                INSERT INTO public.background_jobs (job_type, payload, status, created_at)
                VALUES ('decision_updated_notification', @Payload::jsonb, 'queued', @Now)
                """,
                new
                {
                    Payload = JsonSerializer.Serialize(new
                    {
                        DecisionId = request.Id, OrganizationId = request.OrgId,
                        NewStatus = request.Status
                    }),
                    Now = now.UtcDateTime
                }, tx);

            // Audit log
            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (organization_id, table_name, record_id, actor_id, action, new_values, old_values)
                VALUES (@OrgId, 'decisions', @RecordId, @ActorId, 'update', @NewValues::jsonb, @OldValues::jsonb)
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
