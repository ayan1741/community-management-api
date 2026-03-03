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

public record CreateFinanceRecordCommand(
    Guid OrgId, Guid CategoryId, string Type,
    decimal Amount, DateOnly RecordDate, string Description,
    string? PaymentMethod
) : IRequest<FinanceRecord>;

public class CreateFinanceRecordCommandHandler : IRequestHandler<CreateFinanceRecordCommand, FinanceRecord>
{
    private readonly IFinanceCategoryRepository _categories;
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;

    public CreateFinanceRecordCommandHandler(
        IFinanceCategoryRepository categories,
        ICurrentUserService currentUser,
        IDbConnectionFactory factory)
    {
        _categories = categories;
        _currentUser = currentUser;
        _factory = factory;
    }

    public async Task<FinanceRecord> Handle(CreateFinanceRecordCommand request, CancellationToken ct)
    {
        // Gelir → Admin, Gider → BoardMember
        var minRole = request.Type == "income" ? MemberRole.Admin : MemberRole.BoardMember;
        await _currentUser.RequireRoleAsync(request.OrgId, minRole, ct);

        if (request.Type is not ("income" or "expense"))
            throw AppException.UnprocessableEntity("Geçersiz tür. 'income' veya 'expense' olmalıdır.");

        if (request.Amount <= 0)
            throw AppException.UnprocessableEntity("Tutar sıfırdan büyük olmalıdır.");

        if (request.Description.Trim().Length < 3)
            throw AppException.UnprocessableEntity("Açıklama en az 3 karakter olmalıdır.");

        if (request.RecordDate > DateOnly.FromDateTime(DateTime.UtcNow))
            throw AppException.UnprocessableEntity("Gelecek tarihli kayıt oluşturulamaz.");

        var category = await _categories.GetByIdAsync(request.CategoryId, ct)
            ?? throw AppException.NotFound("Kategori bulunamadı.");

        if (category.OrganizationId != request.OrgId)
            throw AppException.NotFound("Kategori bulunamadı.");

        if (category.Type != request.Type)
            throw AppException.UnprocessableEntity("Kategori türü ile kayıt türü uyuşmuyor.");

        if (!category.IsActive)
            throw AppException.UnprocessableEntity("Pasif kategoriye kayıt eklenemez.");

        // Alt kategorisi olan ana kategoriye doğrudan kayıt engelle
        var hasChildren = await _categories.HasChildrenAsync(request.CategoryId, ct);
        if (hasChildren)
            throw AppException.UnprocessableEntity("Alt kategorileri olan bir kategoriye doğrudan kayıt eklenemez. Lütfen bir alt kategori seçin.");

        if (request.Type == "expense" && string.IsNullOrWhiteSpace(request.PaymentMethod))
            throw AppException.UnprocessableEntity("Gider kaydı için ödeme yöntemi zorunludur.");

        var currentUserId = _currentUser.UserId;
        var now = DateTimeOffset.UtcNow;

        var record = new FinanceRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = request.OrgId,
            CategoryId = request.CategoryId,
            Type = request.Type,
            Amount = request.Amount,
            RecordDate = request.RecordDate,
            Description = request.Description.Trim(),
            PaymentMethod = request.PaymentMethod,
            IsOpeningBalance = false,
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
                     payment_method, is_opening_balance, created_by, created_at, updated_at)
                VALUES
                    (@Id, @OrganizationId, @CategoryId, @Type, @Amount, @RecordDate, @Description,
                     @PaymentMethod, @IsOpeningBalance, @CreatedBy, @CreatedAt, @UpdatedAt)
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
                    record.PaymentMethod,
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
                    NewValues = JsonSerializer.Serialize(new { record.Type, record.Amount, record.Description, record.CategoryId })
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
