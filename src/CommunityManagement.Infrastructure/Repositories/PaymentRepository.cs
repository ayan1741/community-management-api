using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Common;
using Dapper;
using System.Data;

namespace CommunityManagement.Infrastructure.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private readonly IDbConnectionFactory _factory;

    public PaymentRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    private record PaymentDetailRow(
        Guid Id, Guid UnitDueId, string ReceiptNumber, decimal Amount,
        DateTime PaidAt, string PaymentMethod, Guid? CollectedBy,
        bool IsOverpayment, decimal? OverpaymentAmount, string? Note,
        DateTime CreatedAt
    );

    private record PaymentListRow(
        Guid Id, string ReceiptNumber, decimal Amount,
        DateTime PaidAt, string PaymentMethod,
        string? CollectedByName, bool IsOverpayment, decimal? OverpaymentAmount,
        string? Note, DateTime CreatedAt
    );

    private record PaymentHistoryRow(
        Guid Id, string ReceiptNumber, decimal Amount,
        DateTime PaidAt, string PaymentMethod,
        string? CollectedByName,
        string PeriodName, string DueTypeName,
        string UnitNumber, string BlockName,
        DateTime CreatedAt, long TotalCount
    );

    public async Task<IReadOnlyList<PaymentListItem>> GetByUnitDueIdAsync(Guid unitDueId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT
                py.id, py.receipt_number, py.amount, py.paid_at, py.payment_method,
                p.full_name AS collected_by_name,
                py.is_overpayment, py.overpayment_amount, py.note, py.created_at
            FROM public.payments py
            LEFT JOIN public.profiles p ON p.id = py.collected_by
            WHERE py.unit_due_id = @UnitDueId
              AND py.cancelled_at IS NULL
            ORDER BY py.paid_at DESC
            """;
        var rows = await conn.QueryAsync<PaymentListRow>(sql, new { UnitDueId = unitDueId });
        return rows.Select(r => new PaymentListItem(
            r.Id, r.ReceiptNumber, r.Amount,
            new DateTimeOffset(r.PaidAt, TimeSpan.Zero), r.PaymentMethod,
            r.CollectedByName, r.IsOverpayment, r.OverpaymentAmount,
            r.Note, new DateTimeOffset(r.CreatedAt, TimeSpan.Zero)
        )).ToList();
    }

    public async Task<(IReadOnlyList<PaymentHistoryItem> Items, int TotalCount)> GetByResidentAsync(
        Guid orgId, Guid userId, int page, int pageSize, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT
                py.id, py.receipt_number, py.amount, py.paid_at, py.payment_method,
                pc.full_name AS collected_by_name,
                dp.name AS period_name, dt.name AS due_type_name,
                u.unit_number, b.name AS block_name,
                py.created_at, COUNT(*) OVER() AS total_count
            FROM public.payments py
            JOIN public.unit_dues ud ON ud.id = py.unit_due_id
            JOIN public.dues_periods dp ON dp.id = ud.period_id
            JOIN public.due_types dt ON dt.id = ud.due_type_id
            JOIN public.units u ON u.id = ud.unit_id
            JOIN public.blocks b ON b.id = u.block_id
            LEFT JOIN public.profiles pc ON pc.id = py.collected_by
            WHERE dp.organization_id = @OrgId
              AND py.cancelled_at IS NULL
              AND EXISTS (
                SELECT 1 FROM public.unit_residents ur
                WHERE ur.unit_id = ud.unit_id AND ur.user_id = @UserId AND ur.status = 'active'
              )
            ORDER BY py.paid_at DESC
            LIMIT @PageSize OFFSET @Offset
            """;

        var rows = (await conn.QueryAsync<PaymentHistoryRow>(sql, new
        {
            OrgId = orgId,
            UserId = userId,
            PageSize = pageSize,
            Offset = (page - 1) * pageSize
        })).ToList();

        if (rows.Count == 0)
            return (Array.Empty<PaymentHistoryItem>(), 0);

        var totalCount = (int)rows[0].TotalCount;
        var items = rows.Select(r => new PaymentHistoryItem(
            r.Id, r.ReceiptNumber, r.Amount,
            new DateTimeOffset(r.PaidAt, TimeSpan.Zero), r.PaymentMethod,
            r.CollectedByName, r.PeriodName, r.DueTypeName,
            r.UnitNumber, r.BlockName,
            new DateTimeOffset(r.CreatedAt, TimeSpan.Zero)
        )).ToList();

        return (items, totalCount);
    }

    public async Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, unit_due_id, receipt_number, amount, paid_at, payment_method,
                   collected_by, is_overpayment, overpayment_amount, note, created_at
            FROM public.payments
            WHERE id = @Id AND cancelled_at IS NULL
            """;
        var row = await conn.QuerySingleOrDefaultAsync<PaymentDetailRow>(sql, new { Id = id });
        return row is null ? null : MapRow(row);
    }

    public async Task<decimal> GetTotalPaidAsync(Guid unitDueId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT COALESCE(SUM(amount), 0)
            FROM public.payments
            WHERE unit_due_id = @UnitDueId AND cancelled_at IS NULL
            """;
        return await conn.QuerySingleAsync<decimal>(sql, new { UnitDueId = unitDueId });
    }

    public async Task<Payment> CreateAsync(Payment payment, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            INSERT INTO public.payments
                (id, unit_due_id, receipt_number, amount, paid_at, payment_method,
                 collected_by, is_overpayment, overpayment_amount, note, created_at)
            VALUES
                (@Id, @UnitDueId, @ReceiptNumber, @Amount, @PaidAt, @PaymentMethod,
                 @CollectedBy, @IsOverpayment, @OverpaymentAmount, @Note, @CreatedAt)
            RETURNING id, unit_due_id, receipt_number, amount, paid_at, payment_method,
                      collected_by, is_overpayment, overpayment_amount, note, created_at
            """;
        var row = await conn.QuerySingleAsync<PaymentDetailRow>(sql, new
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
        });
        return MapRow(row);
    }

    public async Task UpdateAsync(Payment payment, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            UPDATE public.payments
            SET receipt_number = @ReceiptNumber, amount = @Amount, paid_at = @PaidAt,
                payment_method = @PaymentMethod, note = @Note
            WHERE id = @Id AND cancelled_at IS NULL
            """;
        await conn.ExecuteAsync(sql, new
        {
            payment.Id,
            payment.ReceiptNumber,
            payment.Amount,
            PaidAt = payment.PaidAt.UtcDateTime,
            payment.PaymentMethod,
            payment.Note
        });
    }

    public async Task SoftDeleteAsync(Guid id, Guid cancelledBy, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            UPDATE public.payments
            SET cancelled_at = now(), cancelled_by = @CancelledBy
            WHERE id = @Id AND cancelled_at IS NULL
            """;
        await conn.ExecuteAsync(sql, new { Id = id, CancelledBy = cancelledBy });
    }

    public async Task SoftDeleteByUnitDueIdAsync(Guid unitDueId, Guid cancelledBy, IDbConnection conn, IDbTransaction tx, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE public.payments
            SET cancelled_at = now(), cancelled_by = @CancelledBy
            WHERE unit_due_id = @UnitDueId AND cancelled_at IS NULL
            """;
        await conn.ExecuteAsync(sql, new { UnitDueId = unitDueId, CancelledBy = cancelledBy }, tx);
    }

    private static Payment MapRow(PaymentDetailRow row) => new()
    {
        Id = row.Id,
        UnitDueId = row.UnitDueId,
        ReceiptNumber = row.ReceiptNumber,
        Amount = row.Amount,
        PaidAt = new DateTimeOffset(row.PaidAt, TimeSpan.Zero),
        PaymentMethod = row.PaymentMethod,
        CollectedBy = row.CollectedBy,
        IsOverpayment = row.IsOverpayment,
        OverpaymentAmount = row.OverpaymentAmount,
        Note = row.Note,
        CreatedAt = new DateTimeOffset(row.CreatedAt, TimeSpan.Zero)
    };
}
