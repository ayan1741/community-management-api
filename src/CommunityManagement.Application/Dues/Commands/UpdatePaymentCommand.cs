using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;

namespace CommunityManagement.Application.Dues.Commands;

public record UpdatePaymentCommand(
    Guid OrgId,
    Guid PaymentId,
    string ReceiptNumber,
    decimal Amount,
    DateTimeOffset PaidAt,
    string PaymentMethod,
    string? Note
) : IRequest<Payment>;

public class UpdatePaymentCommandHandler : IRequestHandler<UpdatePaymentCommand, Payment>
{
    private readonly IPaymentRepository _payments;
    private readonly IUnitDueRepository _unitDues;
    private readonly IDuesPeriodRepository _periods;
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;

    public UpdatePaymentCommandHandler(
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

    public async Task<Payment> Handle(UpdatePaymentCommand request, CancellationToken ct)
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

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            // a. Ödeme güncelle
            await conn.ExecuteAsync(
                """
                UPDATE public.payments
                SET receipt_number = @ReceiptNumber, amount = @Amount, paid_at = @PaidAt,
                    payment_method = @PaymentMethod, note = @Note
                WHERE id = @Id AND cancelled_at IS NULL
                """,
                new
                {
                    Id = request.PaymentId,
                    request.ReceiptNumber,
                    request.Amount,
                    PaidAt = request.PaidAt.UtcDateTime,
                    request.PaymentMethod,
                    request.Note
                }, tx);

            // b. Toplam ödemeyi yeniden hesapla ve unit_due.status güncelle
            var totalPaid = await conn.QuerySingleAsync<decimal>(
                "SELECT COALESCE(SUM(amount), 0) FROM public.payments WHERE unit_due_id = @UnitDueId AND cancelled_at IS NULL",
                new { UnitDueId = payment.UnitDueId }, tx);

            var newStatus = totalPaid <= 0 ? "pending"
                : totalPaid >= unitDue.Amount ? "paid"
                : "partial";

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

        payment.ReceiptNumber = request.ReceiptNumber;
        payment.Amount = request.Amount;
        payment.PaidAt = request.PaidAt;
        payment.PaymentMethod = request.PaymentMethod;
        payment.Note = request.Note;
        return payment;
    }
}
