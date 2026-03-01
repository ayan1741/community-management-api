using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;

namespace CommunityManagement.Application.Dues.Commands;

public record SoftDeletePaymentCommand(Guid OrgId, Guid PaymentId) : IRequest;

public class SoftDeletePaymentCommandHandler : IRequestHandler<SoftDeletePaymentCommand>
{
    private readonly IPaymentRepository _payments;
    private readonly IUnitDueRepository _unitDues;
    private readonly IDuesPeriodRepository _periods;
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;

    public SoftDeletePaymentCommandHandler(
        IPaymentRepository payments,
        IUnitDueRepository unitDues,
        IDuesPeriodRepository periods,
        ICurrentUserService currentUser,
        IDbConnectionFactory factory)
    {
        _payments = payments;
        _unitDues = unitDues;
        _periods = periods;
        _currentUser = currentUser;
        _factory = factory;
    }

    public async Task Handle(SoftDeletePaymentCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var payment = await _payments.GetByIdAsync(request.PaymentId, ct)
            ?? throw AppException.NotFound("Ödeme bulunamadı.");

        var unitDue = await _unitDues.GetByIdAsync(payment.UnitDueId, ct)
            ?? throw AppException.NotFound("Tahakkuk bulunamadı.");

        var period = await _periods.GetByIdAsync(unitDue.PeriodId, ct)
            ?? throw AppException.NotFound("Dönem bulunamadı.");

        if (period.OrganizationId != request.OrgId)
            throw AppException.NotFound("Ödeme bulunamadı.");

        var currentUserId = _currentUser.UserId;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            // a. Ödemeyi soft-delete et
            await conn.ExecuteAsync(
                "UPDATE public.payments SET cancelled_at = now(), cancelled_by = @CancelledBy WHERE id = @Id AND cancelled_at IS NULL",
                new { Id = request.PaymentId, CancelledBy = currentUserId }, tx);

            // b. Kalan ödeme tutarını hesapla (bu ödeme artık cancelled, COALESCE ile 0 gelir)
            var remaining = await conn.QuerySingleAsync<decimal>(
                "SELECT COALESCE(SUM(amount), 0) FROM public.payments WHERE unit_due_id = @UnitDueId AND cancelled_at IS NULL",
                new { UnitDueId = payment.UnitDueId }, tx);

            var newStatus = remaining <= 0 ? "pending" : "partial";

            // c. Tahakkuk durumunu güncelle
            await conn.ExecuteAsync(
                "UPDATE public.unit_dues SET status = @Status, updated_at = now() WHERE id = @Id",
                new { Status = newStatus, Id = payment.UnitDueId }, tx);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
