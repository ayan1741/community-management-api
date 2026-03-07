using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.MaintenanceRequests.Commands;

public record TransferCostToFinanceCommand(Guid OrgId, Guid Id, Guid CostId) : IRequest<Guid>;

public class TransferCostToFinanceCommandHandler : IRequestHandler<TransferCostToFinanceCommand, Guid>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IMaintenanceRequestRepository _repo;

    public TransferCostToFinanceCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IMaintenanceRequestRepository repo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
    }

    private record CostRow(Guid Id, decimal Amount, string? Description, Guid? FinanceRecordId);

    public async Task<Guid> Handle(TransferCostToFinanceCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw AppException.NotFound("Ariza bildirimi bulunamadi.");
        if (entity.OrganizationId != request.OrgId)
            throw AppException.NotFound("Ariza bildirimi bulunamadi.");

        var currentUserId = _currentUser.UserId;
        var now = DateTimeOffset.UtcNow;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);

        // Maliyet kaydini al
        var cost = await conn.QuerySingleOrDefaultAsync<CostRow>(
            "SELECT id, amount, description, finance_record_id FROM public.maintenance_request_costs WHERE id = @CostId AND maintenance_request_id = @MrId",
            new { request.CostId, MrId = request.Id });

        if (cost is null)
            throw AppException.NotFound("Maliyet kaydi bulunamadi.");
        if (cost.FinanceRecordId is not null)
            throw AppException.UnprocessableEntity("Bu maliyet zaten gelir-gidere aktarilmis.");

        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            // GET_OR_CREATE finance_category
            var categoryId = await conn.QuerySingleOrDefaultAsync<Guid?>(
                "SELECT id FROM public.finance_categories WHERE organization_id = @OrgId AND name = 'Bakim-Onarim' AND type = 'expense'",
                new { request.OrgId }, tx);

            if (categoryId is null)
            {
                categoryId = Guid.NewGuid();
                await conn.ExecuteAsync(
                    "INSERT INTO public.finance_categories (id, organization_id, name, type, created_at) VALUES (@Id, @OrgId, 'Bakim-Onarim', 'expense', @Now)",
                    new { Id = categoryId.Value, request.OrgId, Now = now.UtcDateTime }, tx);
            }

            // INSERT finance_record
            var financeRecordId = Guid.NewGuid();
            var description = $"[Ariza] {entity.Title}" + (cost.Description is not null ? $" — {cost.Description}" : "");

            await conn.ExecuteAsync(
                """
                INSERT INTO public.finance_records
                    (id, organization_id, type, category_id, description, amount, record_date,
                     period_year, period_month, created_by, created_at, updated_at)
                VALUES (@Id, @OrgId, 'expense', @CategoryId, @Description, @Amount, @RecordDate,
                        @PeriodYear, @PeriodMonth, @CreatedBy, @Now, @Now)
                """,
                new
                {
                    Id = financeRecordId, request.OrgId, CategoryId = categoryId.Value,
                    Description = description, cost.Amount,
                    RecordDate = now.UtcDateTime,
                    PeriodYear = now.Year, PeriodMonth = now.Month,
                    CreatedBy = currentUserId, Now = now.UtcDateTime
                }, tx);

            // UPDATE maintenance_request_costs
            await conn.ExecuteAsync(
                "UPDATE public.maintenance_request_costs SET finance_record_id = @FrId WHERE id = @CostId",
                new { FrId = financeRecordId, request.CostId }, tx);

            // Audit log
            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (organization_id, table_name, record_id, actor_id, action, new_values)
                VALUES (@OrgId, 'maintenance_request_costs', @RecordId, @ActorId, 'update', @NewValues::jsonb)
                """,
                new
                {
                    OrgId = request.OrgId, RecordId = request.CostId, ActorId = currentUserId,
                    NewValues = JsonSerializer.Serialize(new { FinanceRecordId = financeRecordId })
                }, tx);

            await tx.CommitAsync(ct);
            return financeRecordId;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
