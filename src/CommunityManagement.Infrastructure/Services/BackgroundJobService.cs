using CommunityManagement.Core.Common;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CommunityManagement.Infrastructure.Services;

/// <summary>
/// Genel background job işleyicisi: due_reminder, late_notice, payment_email job tiplerini işler.
/// BulkAccrualService, bulk_accrual job tipini ayrıca işler.
/// </summary>
public class BackgroundJobService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackgroundJobService> _logger;

    public BackgroundJobService(IServiceScopeFactory scopeFactory, ILogger<BackgroundJobService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessPendingJobsAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ContinueWith(_ => { });
        }
    }

    private async Task ProcessPendingJobsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

            using var conn = factory.CreateServiceRoleConnection();
            var jobs = (await conn.QueryAsync<JobRow>(
                """
                SELECT id, job_type, payload
                FROM public.background_jobs
                WHERE job_type IN ('due_reminder','late_notice','payment_email','payment_cancel_email')
                  AND status = 'queued'
                ORDER BY created_at ASC
                LIMIT 10
                """)).ToList();

            foreach (var job in jobs)
            {
                if (ct.IsCancellationRequested) break;
                await ProcessJobAsync(factory, job, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BackgroundJobService polling hatası.");
        }
    }

    private async Task ProcessJobAsync(IDbConnectionFactory factory, JobRow job, CancellationToken ct)
    {
        _logger.LogInformation("Background job başlatıldı: {JobType} / {JobId}", job.JobType, job.Id);

        using var conn = factory.CreateServiceRoleConnection();
        await conn.ExecuteAsync(
            "UPDATE public.background_jobs SET status = 'running' WHERE id = @Id",
            new { job.Id });

        try
        {
            switch (job.JobType)
            {
                case "due_reminder":
                    await ProcessDueReminderAsync(factory, job, ct);
                    break;
                case "late_notice":
                    await ProcessLateNoticeAsync(factory, job, ct);
                    break;
                case "payment_email":
                case "payment_cancel_email":
                    await ProcessPaymentEmailAsync(factory, job, ct);
                    break;
                default:
                    _logger.LogWarning("Bilinmeyen job tipi: {JobType}", job.JobType);
                    break;
            }

            await conn.ExecuteAsync(
                "UPDATE public.background_jobs SET status = 'completed', completed_at = now() WHERE id = @Id",
                new { job.Id });

            _logger.LogInformation("Background job tamamlandı: {JobType} / {JobId}", job.JobType, job.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background job hata: {JobType} / {JobId}", job.JobType, job.Id);
            await conn.ExecuteAsync(
                "UPDATE public.background_jobs SET status = 'failed', error_details = @Error::jsonb, completed_at = now() WHERE id = @Id",
                new { job.Id, Error = JsonSerializer.Serialize(new { error = ex.Message }) });
        }
    }

    private async Task ProcessDueReminderAsync(IDbConnectionFactory factory, JobRow job, CancellationToken ct)
    {
        // Payload: { period_id, organization_id }
        var payload = JsonSerializer.Deserialize<JsonElement>(job.Payload);
        var periodId = payload.GetProperty("period_id").GetGuid();

        using var conn = factory.CreateServiceRoleConnection();

        // Borçlu sakinleri bul ve email gönder (STUB — Phase 3d'de gerçek email entegrasyonu)
        var borçluSakinler = await conn.QueryAsync<string>(
            """
            SELECT DISTINCT p.full_name
            FROM public.unit_dues ud
            JOIN public.unit_residents ur ON ur.unit_id = ud.unit_id AND ur.status = 'active'
            JOIN public.profiles p ON p.id = ur.user_id
            WHERE ud.period_id = @PeriodId
              AND ud.status IN ('pending','partial')
              AND p.notification_email = true
            """,
            new { PeriodId = periodId });

        _logger.LogInformation("due_reminder: {PeriodId} — {Count} sakin için email gönderilecek (STUB)",
            periodId, borçluSakinler.Count());

        // [UYARI-4]: Spam önleme — reminder_sent_at güncelle
        await conn.ExecuteAsync(
            "UPDATE public.dues_periods SET reminder_sent_at = now(), updated_at = now() WHERE id = @PeriodId",
            new { PeriodId = periodId });
    }

    private async Task ProcessLateNoticeAsync(IDbConnectionFactory factory, JobRow job, CancellationToken ct)
    {
        // Payload: { unit_due_id }
        var payload = JsonSerializer.Deserialize<JsonElement>(job.Payload);
        var unitDueId = payload.GetProperty("unit_due_id").GetGuid();

        using var conn = factory.CreateServiceRoleConnection();

        // Sakin bilgilerini al (STUB — Phase 3d'de gerçek email entegrasyonu)
        _logger.LogInformation("late_notice: unitDueId={UnitDueId} — gecikme uyarısı gönderilecek (STUB)", unitDueId);

        // [UYARI-4]: Spam önleme — late_notice_sent_at güncelle
        await conn.ExecuteAsync(
            "UPDATE public.unit_dues SET late_notice_sent_at = now(), updated_at = now() WHERE id = @UnitDueId",
            new { UnitDueId = unitDueId });
    }

    private async Task ProcessPaymentEmailAsync(IDbConnectionFactory factory, JobRow job, CancellationToken ct)
    {
        // Payload: { paymentId, unitDueId }
        var payload = JsonSerializer.Deserialize<JsonElement>(job.Payload);
        var paymentId = payload.GetProperty("paymentId").GetGuid();

        // STUB — Phase 3d'de gerçek email entegrasyonu
        _logger.LogInformation("payment_email: paymentId={PaymentId} — ödeme onay emaili gönderilecek (STUB)", paymentId);
    }

    private record JobRow(Guid Id, string JobType, string Payload);
}
