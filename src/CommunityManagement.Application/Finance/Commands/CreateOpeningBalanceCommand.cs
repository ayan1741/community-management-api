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

public record CreateOpeningBalanceCommand(
    Guid OrgId, decimal Amount, DateOnly RecordDate, string? Description
) : IRequest<FinanceRecord>;

public class CreateOpeningBalanceCommandHandler : IRequestHandler<CreateOpeningBalanceCommand, FinanceRecord>
{
    private readonly IFinanceRecordRepository _records;
    private readonly IFinanceCategoryRepository _categories;
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;

    public CreateOpeningBalanceCommandHandler(
        IFinanceRecordRepository records,
        IFinanceCategoryRepository categories,
        ICurrentUserService currentUser,
        IDbConnectionFactory factory)
    {
        _records = records;
        _categories = categories;
        _currentUser = currentUser;
        _factory = factory;
    }

    public async Task<FinanceRecord> Handle(CreateOpeningBalanceCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        if (request.Amount <= 0)
            throw AppException.UnprocessableEntity("Devir bakiyesi sıfırdan büyük olmalıdır.");

        var hasBalance = await _records.HasOpeningBalanceAsync(request.OrgId, ct);
        if (hasBalance)
            throw AppException.Conflict("Devir bakiyesi zaten girilmiş.");

        // "Devir Bakiyesi" sistem kategorisini bul
        var allCats = await _categories.GetByOrgIdAsync(request.OrgId, "income", null, ct);
        var openingCat = allCats.FirstOrDefault(c => c.IsSystem && c.Name == "Devir Bakiyesi")
            ?? throw AppException.UnprocessableEntity("Devir Bakiyesi kategorisi bulunamadı. Lütfen önce varsayılan kategorileri oluşturun.");

        var currentUserId = _currentUser.UserId;
        var now = DateTimeOffset.UtcNow;

        var record = new FinanceRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = request.OrgId,
            CategoryId = openingCat.Id,
            Type = "income",
            Amount = request.Amount,
            RecordDate = request.RecordDate,
            PeriodYear = request.RecordDate.Year,
            PeriodMonth = request.RecordDate.Month,
            Description = request.Description?.Trim() ?? "Devir bakiyesi",
            IsOpeningBalance = true,
            CreatedBy = currentUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO public.finance_records
                    (id, organization_id, category_id, type, amount, record_date, description,
                     period_year, period_month,
                     is_opening_balance, created_by, created_at, updated_at)
                VALUES
                    (@Id, @OrganizationId, @CategoryId, @Type, @Amount, @RecordDate, @Description,
                     @PeriodYear, @PeriodMonth,
                     @IsOpeningBalance, @CreatedBy, @CreatedAt, @UpdatedAt)
                """,
                new
                {
                    record.Id,
                    record.OrganizationId,
                    record.CategoryId,
                    record.Type,
                    record.Amount,
                    RecordDate = record.RecordDate.ToDateTime(TimeOnly.MinValue),
                    record.Description,
                    record.PeriodYear,
                    record.PeriodMonth,
                    record.IsOpeningBalance,
                    record.CreatedBy,
                    CreatedAt = record.CreatedAt.UtcDateTime,
                    UpdatedAt = record.UpdatedAt.UtcDateTime
                }, tx);

            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (table_name, record_id, actor_id, action, new_values)
                VALUES ('finance_records', @RecordId, @ActorId, 'insert', @NewValues::jsonb)
                """,
                new
                {
                    RecordId = record.Id,
                    ActorId = currentUserId,
                    NewValues = JsonSerializer.Serialize(new { record.Amount, Type = "opening_balance" })
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
