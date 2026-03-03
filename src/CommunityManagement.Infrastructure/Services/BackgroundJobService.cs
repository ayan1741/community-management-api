using CommunityManagement.Core.Common;
using CommunityManagement.Core.Services;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CommunityManagement.Infrastructure.Services;

/// <summary>
/// Genel background job işleyicisi: due_reminder, late_notice, payment_email, payment_cancel_email job tiplerini işler.
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
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

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
                await ProcessJobAsync(factory, emailService, job, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BackgroundJobService polling hatası.");
        }
    }

    private async Task ProcessJobAsync(IDbConnectionFactory factory, IEmailService emailService, JobRow job, CancellationToken ct)
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
                    await ProcessDueReminderAsync(factory, emailService, job, ct);
                    break;
                case "late_notice":
                    await ProcessLateNoticeAsync(factory, emailService, job, ct);
                    break;
                case "payment_email":
                    await ProcessPaymentEmailAsync(factory, emailService, job, ct);
                    break;
                case "payment_cancel_email":
                    await ProcessPaymentCancelEmailAsync(factory, emailService, job, ct);
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

    private async Task ProcessDueReminderAsync(IDbConnectionFactory factory, IEmailService emailService, JobRow job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(job.Payload);
        var periodId = payload.GetProperty("period_id").GetGuid();

        using var conn = factory.CreateServiceRoleConnection();

        // Borçlu sakinlerin bilgilerini ve emaillerini al (auth.users service role ile erişilebilir)
        var recipients = (await conn.QueryAsync<ReminderRecipient>(
            """
            SELECT DISTINCT
                p.full_name,
                au.email,
                COALESCE(SUM(ud.amount), 0) AS total_owed
            FROM public.unit_dues ud
            JOIN public.unit_residents ur ON ur.unit_id = ud.unit_id AND ur.status = 'active'
            JOIN public.profiles p ON p.id = ur.user_id
            JOIN auth.users au ON au.id = ur.user_id
            WHERE ud.period_id = @PeriodId
              AND ud.status IN ('pending','partial')
            GROUP BY p.full_name, au.email
            """,
            new { PeriodId = periodId })).ToList();

        // Dönem adı ve organizasyon adı
        var periodInfo = await conn.QuerySingleOrDefaultAsync<PeriodInfo>(
            """
            SELECT dp.name AS period_name, o.name AS org_name
            FROM public.dues_periods dp
            JOIN public.organizations o ON o.id = dp.organization_id
            WHERE dp.id = @PeriodId
            """,
            new { PeriodId = periodId });

        var periodName = periodInfo?.PeriodName ?? "Bilinmeyen dönem";
        var orgName = periodInfo?.OrgName ?? "Bilinmeyen";

        foreach (var r in recipients)
        {
            if (string.IsNullOrEmpty(r.Email))
            {
                _logger.LogWarning("due_reminder: {FullName} için email adresi bulunamadı, atlanıyor.", r.FullName);
                continue;
            }

            var html = EmailTemplates.DueReminder(orgName, r.FullName, periodName, r.TotalOwed);
            await emailService.SendAsync(r.Email, $"Aidat Hatırlatması — {periodName}", html, ct);
        }

        _logger.LogInformation("due_reminder: {PeriodId} — {Count} sakin için email gönderildi.", periodId, recipients.Count);

        // Spam önleme — reminder_sent_at güncelle
        await conn.ExecuteAsync(
            "UPDATE public.dues_periods SET reminder_sent_at = now(), updated_at = now() WHERE id = @PeriodId",
            new { PeriodId = periodId });
    }

    private async Task ProcessLateNoticeAsync(IDbConnectionFactory factory, IEmailService emailService, JobRow job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(job.Payload);
        var unitDueId = payload.GetProperty("unit_due_id").GetGuid();

        using var conn = factory.CreateServiceRoleConnection();

        var detail = await conn.QuerySingleOrDefaultAsync<LateNoticeDetail>(
            """
            SELECT
                ud.amount,
                ud.due_date,
                dp.name AS period_name,
                o.name AS org_name
            FROM public.unit_dues ud
            JOIN public.dues_periods dp ON dp.id = ud.period_id
            JOIN public.organizations o ON o.id = dp.organization_id
            WHERE ud.id = @UnitDueId
            """,
            new { UnitDueId = unitDueId });

        if (detail is null)
        {
            _logger.LogWarning("late_notice: unitDueId={UnitDueId} tahakkuk bulunamadı.", unitDueId);
            return;
        }

        var recipients = (await conn.QueryAsync<EmailRecipient>(
            """
            SELECT p.full_name, au.email
            FROM public.unit_dues ud
            JOIN public.unit_residents ur ON ur.unit_id = ud.unit_id AND ur.status = 'active'
            JOIN public.profiles p ON p.id = ur.user_id
            JOIN auth.users au ON au.id = ur.user_id
            WHERE ud.id = @UnitDueId
            """,
            new { UnitDueId = unitDueId })).ToList();

        var lateDays = (DateTime.UtcNow - detail.DueDate).Days;

        foreach (var r in recipients)
        {
            if (string.IsNullOrEmpty(r.Email)) continue;
            var html = EmailTemplates.LateNotice(detail.OrgName, r.FullName, detail.PeriodName, detail.Amount, lateDays);
            await emailService.SendAsync(r.Email, $"Gecikmiş Aidat Uyarısı — {detail.PeriodName}", html, ct);
        }

        // Spam önleme — late_notice_sent_at güncelle
        await conn.ExecuteAsync(
            "UPDATE public.unit_dues SET late_notice_sent_at = now(), updated_at = now() WHERE id = @UnitDueId",
            new { UnitDueId = unitDueId });
    }

    private async Task ProcessPaymentEmailAsync(IDbConnectionFactory factory, IEmailService emailService, JobRow job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(job.Payload);
        var paymentId = payload.GetProperty("paymentId").GetGuid();
        var unitDueId = payload.GetProperty("unitDueId").GetGuid();

        using var conn = factory.CreateServiceRoleConnection();

        var detail = await conn.QuerySingleOrDefaultAsync<PaymentEmailDetail>(
            """
            SELECT
                pay.receipt_number,
                pay.amount,
                dp.name AS period_name,
                o.name AS org_name
            FROM public.payments pay
            JOIN public.unit_dues ud ON ud.id = pay.unit_due_id
            JOIN public.dues_periods dp ON dp.id = ud.period_id
            JOIN public.organizations o ON o.id = dp.organization_id
            WHERE pay.id = @PaymentId
            """,
            new { PaymentId = paymentId });

        if (detail is null)
        {
            _logger.LogWarning("payment_email: paymentId={PaymentId} ödeme bulunamadı.", paymentId);
            return;
        }

        var recipients = (await conn.QueryAsync<EmailRecipient>(
            """
            SELECT p.full_name, au.email
            FROM public.unit_dues ud
            JOIN public.unit_residents ur ON ur.unit_id = ud.unit_id AND ur.status = 'active'
            JOIN public.profiles p ON p.id = ur.user_id
            JOIN auth.users au ON au.id = ur.user_id
            WHERE ud.id = @UnitDueId
            """,
            new { UnitDueId = unitDueId })).ToList();

        foreach (var r in recipients)
        {
            if (string.IsNullOrEmpty(r.Email)) continue;
            var html = EmailTemplates.PaymentConfirmation(detail.OrgName, r.FullName, detail.ReceiptNumber, detail.Amount, detail.PeriodName);
            await emailService.SendAsync(r.Email, $"Ödeme Onayı — {detail.ReceiptNumber}", html, ct);
        }
    }

    private async Task ProcessPaymentCancelEmailAsync(IDbConnectionFactory factory, IEmailService emailService, JobRow job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(job.Payload);
        var paymentId = payload.GetProperty("paymentId").GetGuid();
        var unitDueId = payload.GetProperty("unitDueId").GetGuid();

        using var conn = factory.CreateServiceRoleConnection();

        var detail = await conn.QuerySingleOrDefaultAsync<PaymentEmailDetail>(
            """
            SELECT
                pay.receipt_number,
                pay.amount,
                dp.name AS period_name,
                o.name AS org_name
            FROM public.payments pay
            JOIN public.unit_dues ud ON ud.id = pay.unit_due_id
            JOIN public.dues_periods dp ON dp.id = ud.period_id
            JOIN public.organizations o ON o.id = dp.organization_id
            WHERE pay.id = @PaymentId
            """,
            new { PaymentId = paymentId });

        if (detail is null)
        {
            _logger.LogWarning("payment_cancel_email: paymentId={PaymentId} ödeme bulunamadı.", paymentId);
            return;
        }

        var recipients = (await conn.QueryAsync<EmailRecipient>(
            """
            SELECT p.full_name, au.email
            FROM public.unit_dues ud
            JOIN public.unit_residents ur ON ur.unit_id = ud.unit_id AND ur.status = 'active'
            JOIN public.profiles p ON p.id = ur.user_id
            JOIN auth.users au ON au.id = ur.user_id
            WHERE ud.id = @UnitDueId
            """,
            new { UnitDueId = unitDueId })).ToList();

        foreach (var r in recipients)
        {
            if (string.IsNullOrEmpty(r.Email)) continue;
            var html = EmailTemplates.PaymentCancellation(detail.OrgName, r.FullName, detail.ReceiptNumber, detail.Amount);
            await emailService.SendAsync(r.Email, $"Ödeme İptali — {detail.ReceiptNumber}", html, ct);
        }
    }

    private record JobRow(Guid Id, string JobType, string Payload);
    private record ReminderRecipient(string FullName, string? Email, decimal TotalOwed);
    private record EmailRecipient(string FullName, string? Email);
    private record PeriodInfo(string PeriodName, string OrgName);
    private record LateNoticeDetail(decimal Amount, DateTime DueDate, string PeriodName, string OrgName);
    private record PaymentEmailDetail(string ReceiptNumber, decimal Amount, string PeriodName, string OrgName);
}
