using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.Finance.Commands;

public record UpdateOpeningBalanceCommand(
    Guid OrgId, decimal Amount, DateOnly RecordDate, string? Description
) : IRequest<FinanceRecord>;

public class UpdateOpeningBalanceCommandHandler : IRequestHandler<UpdateOpeningBalanceCommand, FinanceRecord>
{
    private readonly IFinanceRecordRepository _records;
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;

    public UpdateOpeningBalanceCommandHandler(
        IFinanceRecordRepository records,
        ICurrentUserService currentUser,
        IDbConnectionFactory factory)
    {
        _records = records;
        _currentUser = currentUser;
        _factory = factory;
    }

    public async Task<FinanceRecord> Handle(UpdateOpeningBalanceCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var record = await _records.GetOpeningBalanceAsync(request.OrgId, ct)
            ?? throw AppException.NotFound("Devir bakiyesi bulunamadı.");

        if (request.Amount <= 0)
            throw AppException.UnprocessableEntity("Devir bakiyesi sıfırdan büyük olmalıdır.");

        var currentUserId = _currentUser.UserId;
        var oldValues = new { record.Amount, record.RecordDate, record.Description };

        record.Amount = request.Amount;
        record.RecordDate = request.RecordDate;
        record.PeriodYear = request.RecordDate.Year;
        record.PeriodMonth = request.RecordDate.Month;
        record.Description = request.Description?.Trim() ?? "Devir bakiyesi";
        record.UpdatedBy = currentUserId;
        record.UpdatedAt = DateTimeOffset.UtcNow;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                """
                UPDATE public.finance_records
                SET amount = @Amount, record_date = @RecordDate, description = @Description,
                    period_year = @PeriodYear, period_month = @PeriodMonth,
                    updated_by = @UpdatedBy, updated_at = @UpdatedAt
                WHERE id = @Id
                """,
                new
                {
                    record.Id,
                    record.Amount,
                    RecordDate = record.RecordDate.ToDateTime(TimeOnly.MinValue),
                    record.Description,
                    record.PeriodYear,
                    record.PeriodMonth,
                    record.UpdatedBy,
                    UpdatedAt = record.UpdatedAt.UtcDateTime
                }, tx);

            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (table_name, record_id, actor_id, action, old_values, new_values)
                VALUES ('finance_records', @RecordId, @ActorId, 'update', @OldValues::jsonb, @NewValues::jsonb)
                """,
                new
                {
                    RecordId = record.Id,
                    ActorId = currentUserId,
                    OldValues = JsonSerializer.Serialize(oldValues),
                    NewValues = JsonSerializer.Serialize(new { record.Amount, record.RecordDate, record.Description })
                }, tx);

            await tx.CommitAsync(ct);
            return record;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
