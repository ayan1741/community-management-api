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

public record UpdateFinanceRecordCommand(
    Guid OrgId, Guid RecordId, Guid CategoryId,
    decimal Amount, DateOnly RecordDate, string Description,
    string? PaymentMethod
) : IRequest<FinanceRecord>;

public class UpdateFinanceRecordCommandHandler : IRequestHandler<UpdateFinanceRecordCommand, FinanceRecord>
{
    private readonly IFinanceRecordRepository _records;
    private readonly IFinanceCategoryRepository _categories;
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;

    public UpdateFinanceRecordCommandHandler(
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

    public async Task<FinanceRecord> Handle(UpdateFinanceRecordCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var record = await _records.GetByIdAsync(request.RecordId, ct)
            ?? throw AppException.NotFound("Kayıt bulunamadı.");

        if (record.OrganizationId != request.OrgId)
            throw AppException.NotFound("Kayıt bulunamadı.");

        if (record.DeletedAt is not null)
            throw AppException.NotFound("Kayıt bulunamadı.");

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

        if (category.Type != record.Type)
            throw AppException.UnprocessableEntity("Kategori türü ile kayıt türü uyuşmuyor.");

        if (!category.IsActive)
            throw AppException.UnprocessableEntity("Pasif kategoriye kayıt atanamaz.");

        if (record.Type == "expense" && string.IsNullOrWhiteSpace(request.PaymentMethod))
            throw AppException.UnprocessableEntity("Gider kaydı için ödeme yöntemi zorunludur.");

        var currentUserId = _currentUser.UserId;
        var oldValues = new { record.Amount, record.Description, record.CategoryId, record.RecordDate };

        record.CategoryId = request.CategoryId;
        record.Amount = request.Amount;
        record.RecordDate = request.RecordDate;
        record.Description = request.Description.Trim();
        record.PaymentMethod = request.PaymentMethod;
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
                SET category_id = @CategoryId, amount = @Amount, record_date = @RecordDate,
                    description = @Description, payment_method = @PaymentMethod,
                    updated_by = @UpdatedBy, updated_at = @UpdatedAt
                WHERE id = @Id
                """,
                new
                {
                    record.Id,
                    record.CategoryId,
                    record.Amount,
                    RecordDate = record.RecordDate.ToDateTime(TimeOnly.MinValue),
                    record.Description,
                    record.PaymentMethod,
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
                    NewValues = JsonSerializer.Serialize(new { record.Amount, record.Description, record.CategoryId, record.RecordDate })
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
