using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Data.Common;

namespace CommunityManagement.Application.Dues.Commands;

public record CancelUnitDueCommand(
    Guid OrgId,
    Guid PeriodId,
    Guid UnitDueId,
    bool ConfirmCancellation
) : IRequest;

public class CancelUnitDueCommandHandler : IRequestHandler<CancelUnitDueCommand>
{
    private readonly IDuesPeriodRepository _periods;
    private readonly IUnitDueRepository _unitDues;
    private readonly IPaymentRepository _payments;
    private readonly ILateFeeRepository _lateFees;
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;

    public CancelUnitDueCommandHandler(
        IDuesPeriodRepository periods,
        IUnitDueRepository unitDues,
        IPaymentRepository payments,
        ILateFeeRepository lateFees,
        ICurrentUserService currentUser,
        IDbConnectionFactory factory)
    {
        _periods = periods;
        _unitDues = unitDues;
        _payments = payments;
        _lateFees = lateFees;
        _currentUser = currentUser;
        _factory = factory;
    }

    public async Task Handle(CancelUnitDueCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var period = await _periods.GetByIdAsync(request.PeriodId, ct)
            ?? throw AppException.NotFound("Dönem bulunamadı.");

        if (period.OrganizationId != request.OrgId)
            throw AppException.NotFound("Dönem bulunamadı.");

        var unitDue = await _unitDues.GetByIdAsync(request.UnitDueId, ct)
            ?? throw AppException.NotFound("Tahakkuk bulunamadı.");

        if (unitDue.PeriodId != request.PeriodId)
            throw AppException.NotFound("Tahakkuk bulunamadı.");

        if (unitDue.Status == "cancelled")
            throw AppException.UnprocessableEntity("Tahakkuk zaten iptal edilmiş.");

        if (unitDue.Status == "paid")
            throw AppException.UnprocessableEntity("Tam ödenmiş tahakkuk iptal edilemez.");

        var totalPaid = await _payments.GetTotalPaidAsync(request.UnitDueId, ct);
        if (totalPaid > 0 && !request.ConfirmCancellation)
            throw AppException.UnprocessableEntity(
                $"Bu tahakkuka {totalPaid:F2} TL ödeme yapılmış — iptal etmek ödemeyi de iptal sayar. Onaylamak için tekrar gönderin.");

        var currentUserId = _currentUser.UserId;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            // [BLOCKER-1]: Fiziksel DELETE değil — soft-delete (audit trail korunur)
            await _lateFees.CancelByUnitDueIdAsync(request.UnitDueId, currentUserId, conn, tx, ct);
            await _payments.SoftDeleteByUnitDueIdAsync(request.UnitDueId, currentUserId, conn, tx, ct);
            await _unitDues.CancelWithLateFeesAsync(request.UnitDueId, currentUserId, conn, tx, ct);

            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (table_name, record_id, actor_id, action, new_values)
                VALUES ('unit_dues', @RecordId, @ActorId, 'cancel', '{"status":"cancelled"}'::jsonb)
                """,
                new { RecordId = request.UnitDueId, ActorId = currentUserId }, tx);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
