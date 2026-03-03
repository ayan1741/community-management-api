using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.Finance.Commands;

public record SoftDeleteFinanceRecordCommand(
    Guid OrgId, Guid RecordId
) : IRequest;

public class SoftDeleteFinanceRecordCommandHandler : IRequestHandler<SoftDeleteFinanceRecordCommand>
{
    private readonly IFinanceRecordRepository _records;
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;

    public SoftDeleteFinanceRecordCommandHandler(
        IFinanceRecordRepository records,
        ICurrentUserService currentUser,
        IDbConnectionFactory factory)
    {
        _records = records;
        _currentUser = currentUser;
        _factory = factory;
    }

    public async Task Handle(SoftDeleteFinanceRecordCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var record = await _records.GetByIdAsync(request.RecordId, ct)
            ?? throw AppException.NotFound("Kayıt bulunamadı.");

        if (record.OrganizationId != request.OrgId)
            throw AppException.NotFound("Kayıt bulunamadı.");

        if (record.DeletedAt is not null)
            throw AppException.NotFound("Kayıt bulunamadı.");

        if (record.IsOpeningBalance)
            throw AppException.UnprocessableEntity("Devir bakiyesi silinemez.");

        var currentUserId = _currentUser.UserId;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                """
                UPDATE public.finance_records
                SET deleted_at = now(), deleted_by = @DeletedBy, updated_at = now()
                WHERE id = @Id AND deleted_at IS NULL
                """,
                new { Id = request.RecordId, DeletedBy = currentUserId }, tx);

            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (table_name, record_id, actor_id, action, old_values)
                VALUES ('finance_records', @RecordId, @ActorId, 'soft_delete', @OldValues::jsonb)
                """,
                new
                {
                    RecordId = request.RecordId,
                    ActorId = currentUserId,
                    OldValues = JsonSerializer.Serialize(new { record.Amount, record.Description, record.Type })
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
