using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.MaintenanceRequests.Commands;

public record AddMaintenanceRequestCostCommand(
    Guid OrgId, Guid Id, decimal Amount, string? Description
) : IRequest<MaintenanceRequestCost>;

public class AddMaintenanceRequestCostCommandHandler : IRequestHandler<AddMaintenanceRequestCostCommand, MaintenanceRequestCost>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IMaintenanceRequestRepository _repo;

    public AddMaintenanceRequestCostCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IMaintenanceRequestRepository repo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
    }

    public async Task<MaintenanceRequestCost> Handle(AddMaintenanceRequestCostCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.BoardMember, ct);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw AppException.NotFound("Ariza bildirimi bulunamadi.");
        if (entity.OrganizationId != request.OrgId)
            throw AppException.NotFound("Ariza bildirimi bulunamadi.");

        if (request.Amount <= 0)
            throw AppException.UnprocessableEntity("Tutar sifirdan buyuk olmalidir.");

        var currentUserId = _currentUser.UserId;
        var now = DateTimeOffset.UtcNow;

        var cost = new MaintenanceRequestCost
        {
            Id = Guid.NewGuid(),
            MaintenanceRequestId = request.Id,
            Amount = request.Amount,
            Description = request.Description?.Trim(),
            CreatedBy = currentUserId,
            CreatedAt = now
        };

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO public.maintenance_request_costs
                    (id, maintenance_request_id, amount, description, created_by, created_at)
                VALUES (@Id, @MaintenanceRequestId, @Amount, @Description, @CreatedBy, @CreatedAt)
                """,
                new
                {
                    cost.Id, cost.MaintenanceRequestId, cost.Amount, cost.Description,
                    cost.CreatedBy, CreatedAt = cost.CreatedAt.UtcDateTime
                }, tx);

            // total_cost guncelle
            await conn.ExecuteAsync(
                """
                UPDATE public.maintenance_requests
                SET total_cost = (SELECT COALESCE(SUM(amount), 0) FROM maintenance_request_costs WHERE maintenance_request_id = @Id),
                    updated_at = @Now
                WHERE id = @Id
                """,
                new { Id = request.Id, Now = now.UtcDateTime }, tx);

            // Audit log
            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (organization_id, table_name, record_id, actor_id, action, new_values)
                VALUES (@OrgId, 'maintenance_request_costs', @RecordId, @ActorId, 'insert', @NewValues::jsonb)
                """,
                new
                {
                    OrgId = request.OrgId, RecordId = cost.Id, ActorId = currentUserId,
                    NewValues = JsonSerializer.Serialize(new { cost.Amount, cost.Description, MaintenanceRequestId = request.Id })
                }, tx);

            await tx.CommitAsync(ct);
            return cost;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
