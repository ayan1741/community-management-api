using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Common;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CommunityManagement.Infrastructure.Services;

public class BulkAccrualService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BulkAccrualService> _logger;

    public BulkAccrualService(IServiceScopeFactory scopeFactory, ILogger<BulkAccrualService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessPendingJobsAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ContinueWith(_ => { });
        }
    }

    private async Task ProcessPendingJobsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

            // Claim up to 5 jobs atomically: SELECT FOR UPDATE SKIP LOCKED + mark 'running'
            await using var claimConn = factory.CreateServiceRoleDbConnection();
            await claimConn.OpenAsync(ct);
            await using var claimTx = await claimConn.BeginTransactionAsync(ct);

            var jobs = (await claimConn.QueryAsync<JobRow>(
                """
                SELECT id, payload
                FROM public.background_jobs
                WHERE job_type = 'bulk_accrual' AND status = 'queued'
                ORDER BY created_at ASC
                LIMIT 5
                FOR UPDATE SKIP LOCKED
                """, transaction: claimTx)).ToList();

            if (jobs.Count > 0)
            {
                var ids = jobs.Select(j => j.Id).ToArray();
                await claimConn.ExecuteAsync(
                    "UPDATE public.background_jobs SET status = 'running' WHERE id = ANY(@Ids)",
                    new { Ids = ids }, claimTx);
            }

            await claimTx.CommitAsync(ct);

            foreach (var job in jobs)
            {
                if (ct.IsCancellationRequested) break;
                await ProcessJobAsync(factory, job, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BulkAccrualService polling hatası.");
        }
    }

    private async Task ProcessJobAsync(IDbConnectionFactory factory, JobRow job, CancellationToken ct)
    {
        _logger.LogInformation("BulkAccrual job başlatıldı: {JobId}", job.Id);

        BulkAccrualPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<BulkAccrualPayload>(job.Payload)
                ?? throw new InvalidOperationException("Payload parse edilemedi.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BulkAccrual job payload parse hatası: {JobId}", job.Id);
            await FailJobAsync(factory, job.Id, "Payload parse hatası: " + ex.Message);
            return;
        }

        // Ana transaction: unit_dues INSERT
        await using var conn = factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            var units = (await conn.QueryAsync<AccrualUnitRow>(
                """
                SELECT u.id, u.unit_number, u.unit_category,
                       EXISTS(SELECT 1 FROM public.unit_residents ur
                              WHERE ur.unit_id = u.id AND ur.status = 'active') AS is_occupied
                FROM public.units u
                WHERE u.organization_id = @OrgId AND u.deleted_at IS NULL
                """,
                new { payload.OrgId }, tx)).ToList();

            var dueTypes = (await conn.QueryAsync<DueTypeRow>(
                """
                SELECT id, name, default_amount, category_amounts
                FROM public.due_types
                WHERE id = ANY(@Ids) AND organization_id = @OrgId AND is_active = true
                """,
                new { Ids = payload.DueTypeIds.ToArray(), payload.OrgId }, tx)).ToList();

            var includedUnits = payload.IncludeEmptyUnits
                ? units
                : units.Where(u => u.IsOccupied).ToList();

            var now = DateTime.UtcNow;
            var unitDues = new List<UnitDue>();

            foreach (var dt in dueTypes)
            {
                var categoryMap = ParseCategoryAmounts(dt.CategoryAmounts);
                foreach (var unit in includedUnits)
                {
                    var amount = unit.UnitCategory is not null && categoryMap.TryGetValue(unit.UnitCategory, out var catAmt)
                        ? catAmt
                        : dt.DefaultAmount;

                    unitDues.Add(new UnitDue
                    {
                        Id = Guid.NewGuid(),
                        PeriodId = payload.PeriodId,
                        UnitId = unit.Id,
                        DueTypeId = dt.Id,
                        Amount = amount,
                        Status = "pending",
                        CreatedBy = payload.CreatedBy,
                        CreatedAt = new DateTimeOffset(now, TimeSpan.Zero),
                        UpdatedAt = new DateTimeOffset(now, TimeSpan.Zero)
                    });
                }
            }

            const string insertSql = """
                INSERT INTO public.unit_dues
                    (id, period_id, unit_id, due_type_id, amount, status, created_by, created_at, updated_at)
                VALUES
                    (@Id, @PeriodId, @UnitId, @DueTypeId, @Amount, @Status, @CreatedBy, @CreatedAt, @UpdatedAt)
                ON CONFLICT (period_id, unit_id, due_type_id) DO NOTHING
                """;

            foreach (var ud in unitDues)
            {
                await conn.ExecuteAsync(insertSql, new
                {
                    ud.Id, ud.PeriodId, ud.UnitId, ud.DueTypeId, ud.Amount, ud.Status, ud.CreatedBy,
                    CreatedAt = ud.CreatedAt.UtcDateTime,
                    UpdatedAt = ud.UpdatedAt.UtcDateTime
                }, tx);
            }

            await tx.CommitAsync(ct);
            _logger.LogInformation("BulkAccrual unit_dues INSERT tamamlandı: {Count} tahakkuk, JobId={JobId}", unitDues.Count, job.Id);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "BulkAccrual unit_dues INSERT hatası, period 'failed' yapılıyor: {JobId}", job.Id);

            await FailJobAsync(factory, job.Id, ex.Message);

            await using var failConn = factory.CreateServiceRoleDbConnection();
            await failConn.OpenAsync(ct);
            await failConn.ExecuteAsync(
                "UPDATE public.dues_periods SET status = 'failed', updated_at = now() WHERE id = @Id",
                new { Id = payload.PeriodId });
            return;
        }

        // Ayrı transaction: period → 'active', job → 'completed'
        try
        {
            await using var completeConn = factory.CreateServiceRoleDbConnection();
            await completeConn.OpenAsync(ct);
            await completeConn.ExecuteAsync(
                """
                UPDATE public.dues_periods SET status = 'active', updated_at = now() WHERE id = @Id;
                UPDATE public.background_jobs SET status = 'completed', completed_at = now() WHERE id = @JobId;
                """,
                new { Id = payload.PeriodId, JobId = job.Id });

            _logger.LogInformation("BulkAccrual job tamamlandı: {JobId}", job.Id);
        }
        catch (Exception ex)
        {
            // unit_dues başarıyla oluşturuldu ama job completion başarısız
            // Log et — dönem aktif, job 'running' kalır, idempotent retry güvenli
            _logger.LogError(ex, "BulkAccrual job completion hatası (unit_dues OK): {JobId}", job.Id);
        }
    }

    private async Task FailJobAsync(IDbConnectionFactory factory, Guid jobId, string error)
    {
        await using var conn = factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE public.background_jobs SET status = 'failed', error_details = @Error::jsonb, completed_at = now() WHERE id = @Id",
            new { Id = jobId, Error = JsonSerializer.Serialize(new { error }) });
    }

    private static Dictionary<string, decimal> ParseCategoryAmounts(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new Dictionary<string, decimal>();
        try { return JsonSerializer.Deserialize<Dictionary<string, decimal>>(json) ?? new Dictionary<string, decimal>(); }
        catch { return new Dictionary<string, decimal>(); }
    }

    private record JobRow(Guid Id, string Payload);
    private record AccrualUnitRow(Guid Id, string UnitNumber, string? UnitCategory, bool IsOccupied);
    private record DueTypeRow(Guid Id, string Name, decimal DefaultAmount, string? CategoryAmounts);

    private class BulkAccrualPayload
    {
        public Guid PeriodId { get; set; }
        public Guid OrgId { get; set; }
        public List<Guid> DueTypeIds { get; set; } = [];
        public bool IncludeEmptyUnits { get; set; }
        public Guid CreatedBy { get; set; }
    }
}
