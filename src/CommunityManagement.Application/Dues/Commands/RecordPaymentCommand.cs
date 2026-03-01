using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Data.Common;
using System.Text.Json;

namespace CommunityManagement.Application.Dues.Commands;

public record RecordPaymentCommand(
    Guid OrgId,
    Guid UnitDueId,
    decimal Amount,
    DateTimeOffset PaidAt,
    string PaymentMethod,
    string? Note,
    bool Confirmed
) : IRequest<Payment>;

public class RecordPaymentCommandHandler : IRequestHandler<RecordPaymentCommand, Payment>
{
    private readonly IUnitDueRepository _unitDues;
    private readonly IPaymentRepository _payments;
    private readonly IDuesPeriodRepository _periods;
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;

    public RecordPaymentCommandHandler(
        IUnitDueRepository unitDues,
        IPaymentRepository payments,
        IDuesPeriodRepository periods,
        ICurrentUserService currentUser,
        IDbConnectionFactory factory)
    {
        _unitDues = unitDues;
        _payments = payments;
        _periods = periods;
        _currentUser = currentUser;
        _factory = factory;
    }

    public async Task<Payment> Handle(RecordPaymentCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.BoardMember, ct);

        var unitDue = await _unitDues.GetByIdAsync(request.UnitDueId, ct)
            ?? throw AppException.NotFound("Tahakkuk bulunamadı.");

        // Dönem org sahiplik kontrolü
        var period = await _periods.GetByIdAsync(unitDue.PeriodId, ct)
            ?? throw AppException.NotFound("Dönem bulunamadı.");

        if (period.OrganizationId != request.OrgId)
            throw AppException.NotFound("Tahakkuk bulunamadı.");

        if (unitDue.Status is "cancelled" or "paid")
            throw AppException.UnprocessableEntity("İptal edilmiş veya ödenmiş tahakkuka ödeme eklenemez.");

        var totalPaid = await _payments.GetTotalPaidAsync(request.UnitDueId, ct);
        var newTotal = totalPaid + request.Amount;

        bool isOverpayment = false;
        decimal? overpaymentAmount = null;

        if (newTotal > unitDue.Amount)
        {
            if (!request.Confirmed)
                throw AppException.UnprocessableEntity(
                    $"Girilen tutar tahakkuku aşıyor (Toplam: {newTotal:F2} TL, Tahakkuk: {unitDue.Amount:F2} TL). Fazla ödeme kaydı için onaylamanız gerekiyor.");

            isOverpayment = true;
            overpaymentAmount = newTotal - unitDue.Amount;
        }

        var receiptNumber = $"{request.PaidAt:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpper()}";
        var currentUserId = _currentUser.UserId;

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            UnitDueId = request.UnitDueId,
            ReceiptNumber = receiptNumber,
            Amount = request.Amount,
            PaidAt = request.PaidAt,
            PaymentMethod = request.PaymentMethod,
            CollectedBy = currentUserId,
            IsOverpayment = isOverpayment,
            OverpaymentAmount = overpaymentAmount,
            Note = request.Note,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var newStatus = newTotal >= unitDue.Amount ? "paid" : "partial";

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            // a. Payment INSERT
            await conn.ExecuteAsync(
                """
                INSERT INTO public.payments
                    (id, unit_due_id, receipt_number, amount, paid_at, payment_method,
                     collected_by, is_overpayment, overpayment_amount, note, created_at)
                VALUES
                    (@Id, @UnitDueId, @ReceiptNumber, @Amount, @PaidAt, @PaymentMethod,
                     @CollectedBy, @IsOverpayment, @OverpaymentAmount, @Note, @CreatedAt)
                """,
                new
                {
                    payment.Id,
                    payment.UnitDueId,
                    payment.ReceiptNumber,
                    payment.Amount,
                    PaidAt = payment.PaidAt.UtcDateTime,
                    payment.PaymentMethod,
                    payment.CollectedBy,
                    payment.IsOverpayment,
                    payment.OverpaymentAmount,
                    payment.Note,
                    CreatedAt = payment.CreatedAt.UtcDateTime
                }, tx);

            // b. UnitDue status güncelle
            await conn.ExecuteAsync(
                "UPDATE public.unit_dues SET status = @Status, updated_at = now() WHERE id = @Id",
                new { Status = newStatus, Id = request.UnitDueId }, tx);

            // c. Audit log
            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (table_name, record_id, actor_id, action, new_values)
                VALUES ('payments', @RecordId, @ActorId, 'insert', @NewValues::jsonb)
                """,
                new
                {
                    RecordId = payment.Id,
                    ActorId = currentUserId,
                    NewValues = JsonSerializer.Serialize(new { payment.ReceiptNumber, payment.Amount, payment.PaymentMethod })
                }, tx);

            // d. Background job: email bildirimi
            await conn.ExecuteAsync(
                """
                INSERT INTO public.background_jobs (job_type, payload, status)
                VALUES ('payment_email', @Payload::jsonb, 'queued')
                """,
                new
                {
                    Payload = JsonSerializer.Serialize(new
                    {
                        paymentId = payment.Id,
                        unitDueId = request.UnitDueId
                    })
                }, tx);

            await tx.CommitAsync(ct);
            return payment;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
