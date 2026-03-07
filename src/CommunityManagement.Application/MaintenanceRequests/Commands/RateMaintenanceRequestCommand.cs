using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.MaintenanceRequests.Commands;

public record RateMaintenanceRequestCommand(
    Guid OrgId, Guid Id, short Rating, string? Comment
) : IRequest;

public class RateMaintenanceRequestCommandHandler : IRequestHandler<RateMaintenanceRequestCommand>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IMaintenanceRequestRepository _repo;

    public RateMaintenanceRequestCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IMaintenanceRequestRepository repo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
    }

    public async Task Handle(RateMaintenanceRequestCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw AppException.NotFound("Ariza bildirimi bulunamadi.");
        if (entity.OrganizationId != request.OrgId)
            throw AppException.NotFound("Ariza bildirimi bulunamadi.");

        var currentUserId = _currentUser.UserId;
        if (entity.ReportedBy != currentUserId)
            throw AppException.Forbidden("Sadece bildiren kisi puan verebilir.");
        if (entity.Status != "resolved")
            throw AppException.UnprocessableEntity("Sadece cozulmus arizalara puan verilebilir.");
        if (entity.SatisfactionRating is not null)
            throw AppException.UnprocessableEntity("Puan zaten verilmis.");
        if (request.Rating < 1 || request.Rating > 5)
            throw AppException.UnprocessableEntity("Puan 1-5 arasinda olmalidir.");

        var now = DateTimeOffset.UtcNow;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                """
                UPDATE public.maintenance_requests
                SET satisfaction_rating = @Rating, satisfaction_comment = @Comment,
                    rated_at = @Now, status = 'closed', closed_at = @Now,
                    updated_by = @UserId, updated_at = @Now
                WHERE id = @Id
                """,
                new { request.Rating, request.Comment, Now = now.UtcDateTime, UserId = currentUserId, request.Id }, tx);

            // Log kaydi
            await conn.ExecuteAsync(
                """
                INSERT INTO public.maintenance_request_logs
                    (id, maintenance_request_id, from_status, to_status, note, created_by, created_at)
                VALUES (@LogId, @MrId, 'resolved', 'closed', 'Sakin puan verdi', @UserId, @Now)
                """,
                new { LogId = Guid.NewGuid(), MrId = request.Id, UserId = currentUserId, Now = now.UtcDateTime }, tx);

            // Audit log
            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (organization_id, table_name, record_id, actor_id, action, new_values)
                VALUES (@OrgId, 'maintenance_requests', @RecordId, @ActorId, 'update', @NewValues::jsonb)
                """,
                new
                {
                    OrgId = request.OrgId, RecordId = request.Id, ActorId = currentUserId,
                    NewValues = JsonSerializer.Serialize(new { SatisfactionRating = request.Rating, Status = "closed" })
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
