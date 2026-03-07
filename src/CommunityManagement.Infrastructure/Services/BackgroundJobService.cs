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
                WHERE job_type IN ('due_reminder','late_notice','payment_email','payment_cancel_email','monthly_finance_summary','announcement_email','maintenance_status_email','maintenance_sla_email')
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
                case "monthly_finance_summary":
                    await ProcessMonthlyFinanceSummaryAsync(factory, emailService, job, ct);
                    break;
                case "announcement_email":
                    await ProcessAnnouncementEmailAsync(factory, emailService, job, ct);
                    break;
                case "maintenance_status_email":
                    await ProcessMaintenanceStatusEmailAsync(factory, emailService, job, ct);
                    break;
                case "maintenance_sla_email":
                    await ProcessMaintenanceSlaEmailAsync(factory, emailService, job, ct);
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

    private async Task ProcessMonthlyFinanceSummaryAsync(IDbConnectionFactory factory, IEmailService emailService, JobRow job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(job.Payload);
        var orgId = payload.GetProperty("organization_id").GetGuid();
        var year = payload.GetProperty("year").GetInt32();
        var month = payload.GetProperty("month").GetInt32();

        using var conn = factory.CreateServiceRoleConnection();

        var orgName = await conn.QuerySingleOrDefaultAsync<string>(
            "SELECT name FROM public.organizations WHERE id = @OrgId",
            new { OrgId = orgId });

        if (orgName is null)
        {
            _logger.LogWarning("monthly_finance_summary: orgId={OrgId} organizasyon bulunamadı.", orgId);
            return;
        }

        // Aylık toplamlar (finance_records)
        var totals = await conn.QuerySingleAsync<FinanceTotalsRow>(
            """
            SELECT
              COALESCE(SUM(CASE WHEN type = 'income' THEN amount ELSE 0 END), 0) AS total_income,
              COALESCE(SUM(CASE WHEN type = 'expense' THEN amount ELSE 0 END), 0) AS total_expense
            FROM public.finance_records
            WHERE organization_id = @OrgId AND deleted_at IS NULL
              AND EXTRACT(YEAR FROM record_date) = @Year
              AND EXTRACT(MONTH FROM record_date) = @Month
            """,
            new { OrgId = orgId, Year = year, Month = month });

        // Aidat tahsilatı (payments)
        var duesCollected = await conn.QuerySingleAsync<decimal>(
            """
            SELECT COALESCE(SUM(p.amount), 0)
            FROM public.payments p
            JOIN public.unit_dues ud ON ud.id = p.unit_due_id
            JOIN public.dues_periods dp ON dp.id = ud.period_id
            WHERE dp.organization_id = @OrgId
              AND p.cancelled_at IS NULL
              AND p.paid_at >= make_date(@Year, @Month, 1)::timestamptz
              AND p.paid_at < (make_date(@Year, @Month, 1) + interval '1 month')::timestamptz
            """,
            new { OrgId = orgId, Year = year, Month = month });

        // En büyük 3 gider kategorisi
        var topCategories = (await conn.QueryAsync<TopCategoryRow>(
            """
            SELECT fc.name, SUM(fr.amount) AS amount
            FROM public.finance_records fr
            JOIN public.finance_categories fc ON fc.id = fr.category_id
            WHERE fr.organization_id = @OrgId AND fr.deleted_at IS NULL
              AND fr.type = 'expense'
              AND EXTRACT(YEAR FROM fr.record_date) = @Year
              AND EXTRACT(MONTH FROM fr.record_date) = @Month
            GROUP BY fc.name
            ORDER BY SUM(fr.amount) DESC
            LIMIT 3
            """,
            new { OrgId = orgId, Year = year, Month = month })).ToList();

        // Tüm aktif üyelerin emailleri
        var recipients = (await conn.QueryAsync<EmailRecipient>(
            """
            SELECT DISTINCT p.full_name, au.email
            FROM public.organization_members om
            JOIN public.profiles p ON p.id = om.user_id
            JOIN auth.users au ON au.id = om.user_id
            WHERE om.organization_id = @OrgId AND om.status = 'active'
            """,
            new { OrgId = orgId })).ToList();

        var totalIncome = totals.TotalIncome + duesCollected;
        var totalExpense = totals.TotalExpense;
        var netBalance = totalIncome - totalExpense;
        var monthYear = new DateTime(year, month, 1).ToString("MMMM yyyy", new System.Globalization.CultureInfo("tr-TR"));

        var topCatList = topCategories.Select(c => (c.Name, c.Amount)).ToList();

        foreach (var r in recipients)
        {
            if (string.IsNullOrEmpty(r.Email)) continue;
            var html = EmailTemplates.MonthlyFinanceSummary(orgName, r.FullName, monthYear, totalIncome, totalExpense, netBalance, topCatList);
            await emailService.SendAsync(r.Email, $"Aylık Gelir-Gider Özeti — {monthYear}", html, ct);
        }

        _logger.LogInformation("monthly_finance_summary: {OrgId} — {MonthYear} — {Count} üyeye email gönderildi.", orgId, monthYear, recipients.Count);
    }

    private async Task ProcessAnnouncementEmailAsync(IDbConnectionFactory factory, IEmailService emailService, JobRow job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(job.Payload);
        var announcementId = payload.GetProperty("announcement_id").GetGuid();
        var orgId = payload.GetProperty("organization_id").GetGuid();

        using var conn = factory.CreateServiceRoleConnection();

        // Duyuru bilgilerini al
        var ann = await conn.QuerySingleOrDefaultAsync<AnnouncementEmailInfo>(
            """
            SELECT a.title, a.body, a.category, a.target_type, a.target_ids::text AS target_ids,
                   o.name AS org_name, p.full_name AS author_name
            FROM public.announcements a
            JOIN public.organizations o ON o.id = a.organization_id
            JOIN public.profiles p ON p.id = a.created_by
            WHERE a.id = @Id AND a.status = 'published' AND a.deleted_at IS NULL
            """,
            new { Id = announcementId });

        if (ann is null)
        {
            _logger.LogWarning("announcement_email: {AnnouncementId} duyuru bulunamadı veya silinmiş.", announcementId);
            return;
        }

        // Hedef kitlenin email'lerini al (notification_email = true olanlar)
        string recipientSql;
        object recipientParam;

        switch (ann.TargetType)
        {
            case "block":
                var blockIds = JsonSerializer.Deserialize<List<string>>(ann.TargetIds!)!;
                recipientSql = """
                    SELECT DISTINCT p.full_name, au.email
                    FROM public.organization_members om
                    JOIN public.unit_residents ur ON ur.user_id = om.user_id AND ur.status = 'active'
                    JOIN public.units u ON u.id = ur.unit_id
                    JOIN public.profiles p ON p.id = om.user_id
                    JOIN auth.users au ON au.id = om.user_id
                    WHERE om.organization_id = @OrgId AND om.status = 'active'
                      AND u.block_id = ANY(@BlockIds::uuid[])
                      AND p.notification_email = true
                    """;
                recipientParam = new { OrgId = orgId, BlockIds = blockIds.ToArray() };
                break;

            case "role":
                var roles = JsonSerializer.Deserialize<List<string>>(ann.TargetIds!)!;
                recipientSql = """
                    SELECT DISTINCT p.full_name, au.email
                    FROM public.organization_members om
                    JOIN public.profiles p ON p.id = om.user_id
                    JOIN auth.users au ON au.id = om.user_id
                    WHERE om.organization_id = @OrgId AND om.status = 'active'
                      AND om.role = ANY(@Roles)
                      AND p.notification_email = true
                    """;
                recipientParam = new { OrgId = orgId, Roles = roles.ToArray() };
                break;

            default: // "all"
                recipientSql = """
                    SELECT DISTINCT p.full_name, au.email
                    FROM public.organization_members om
                    JOIN public.profiles p ON p.id = om.user_id
                    JOIN auth.users au ON au.id = om.user_id
                    WHERE om.organization_id = @OrgId AND om.status = 'active'
                      AND p.notification_email = true
                    """;
                recipientParam = new { OrgId = orgId };
                break;
        }

        var recipients = (await conn.QueryAsync<EmailRecipient>(recipientSql, recipientParam)).ToList();

        var sentCount = 0;
        foreach (var r in recipients)
        {
            if (string.IsNullOrEmpty(r.Email)) continue;
            try
            {
                var html = EmailTemplates.AnnouncementPublished(ann.OrgName, r.FullName, ann.Title, ann.Body, ann.Category, ann.AuthorName);
                await emailService.SendAsync(r.Email, $"Yeni Duyuru — {ann.Title}", html, ct);
                sentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "announcement_email: {Email} adresine email gönderilemedi.", r.Email);
            }
        }

        _logger.LogInformation("announcement_email: {AnnouncementId} — {SentCount}/{TotalCount} email gönderildi.",
            announcementId, sentCount, recipients.Count);
    }

    private async Task ProcessMaintenanceStatusEmailAsync(IDbConnectionFactory factory, IEmailService emailService, JobRow job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(job.Payload);
        var mrId = payload.GetProperty("MaintenanceRequestId").GetGuid();
        var orgId = payload.GetProperty("OrganizationId").GetGuid();
        var newStatus = payload.GetProperty("NewStatus").GetString()!;

        using var conn = factory.CreateServiceRoleConnection();

        var mr = await conn.QuerySingleOrDefaultAsync<MaintenanceEmailInfo>(
            """
            SELECT mr.title, mr.category, mr.priority, mr.reported_by,
                   mr.location_type, mr.location_note,
                   o.name AS org_name, p.full_name AS reported_by_name
            FROM public.maintenance_requests mr
            JOIN public.organizations o ON o.id = mr.organization_id
            JOIN public.profiles p ON p.id = mr.reported_by
            WHERE mr.id = @Id AND mr.deleted_at IS NULL
            """,
            new { Id = mrId });

        if (mr is null)
        {
            _logger.LogWarning("maintenance_status_email: {MrId} ariza bulunamadi.", mrId);
            return;
        }

        var locationInfo = mr.LocationType == "common_area" ? "Ortak Alan" : (mr.LocationNote ?? "Daire");
        string? note = payload.TryGetProperty("Note", out var noteProp) ? noteProp.GetString() : null;

        if (newStatus == "reported")
        {
            // Yeni ariza → admin'lere email
            var admins = (await conn.QueryAsync<EmailRecipient>(
                """
                SELECT DISTINCT p.full_name, au.email
                FROM public.organization_members om
                JOIN public.profiles p ON p.id = om.user_id
                JOIN auth.users au ON au.id = om.user_id
                WHERE om.organization_id = @OrgId AND om.role = 'admin' AND om.status = 'active'
                  AND p.notification_email = true
                """,
                new { OrgId = orgId })).ToList();

            foreach (var r in admins)
            {
                if (string.IsNullOrEmpty(r.Email)) continue;
                var html = EmailTemplates.MaintenanceRequestCreated(
                    mr.OrgName, r.FullName, mr.Title, mr.Category, mr.Priority,
                    mr.ReportedByName, locationInfo);
                await emailService.SendAsync(r.Email, $"Yeni Arıza — {mr.Title}", html, ct);
            }
        }
        else if (newStatus == "comment")
        {
            // Yorum → karsi tarafa email
            var commentBy = payload.GetProperty("CommentBy").GetGuid();
            var commentContent = payload.GetProperty("CommentContent").GetString() ?? "";

            if (commentBy == mr.ReportedBy)
            {
                // Sakin yazdi → admin'lere
                var admins = (await conn.QueryAsync<EmailRecipient>(
                    """
                    SELECT DISTINCT p.full_name, au.email
                    FROM public.organization_members om
                    JOIN public.profiles p ON p.id = om.user_id
                    JOIN auth.users au ON au.id = om.user_id
                    WHERE om.organization_id = @OrgId AND om.role = 'admin' AND om.status = 'active'
                      AND p.notification_email = true
                    """,
                    new { OrgId = orgId })).ToList();

                var commentByName = mr.ReportedByName;
                foreach (var r in admins)
                {
                    if (string.IsNullOrEmpty(r.Email)) continue;
                    var html = EmailTemplates.MaintenanceRequestComment(
                        mr.OrgName, r.FullName, mr.Title, commentByName, commentContent);
                    await emailService.SendAsync(r.Email, $"Arıza Yorumu — {mr.Title}", html, ct);
                }
            }
            else
            {
                // Admin yazdi → bildiren sakine
                var commentByProfile = await conn.QuerySingleOrDefaultAsync<string>(
                    "SELECT full_name FROM public.profiles WHERE id = @Id",
                    new { Id = commentBy });
                var commentByName = commentByProfile ?? "Yönetici";

                var recipient = await conn.QuerySingleOrDefaultAsync<EmailRecipient>(
                    """
                    SELECT p.full_name, au.email
                    FROM public.profiles p
                    JOIN auth.users au ON au.id = p.id
                    WHERE p.id = @Id AND p.notification_email = true
                    """,
                    new { Id = mr.ReportedBy });

                if (recipient is not null && !string.IsNullOrEmpty(recipient.Email))
                {
                    var html = EmailTemplates.MaintenanceRequestComment(
                        mr.OrgName, recipient.FullName, mr.Title, commentByName, commentContent);
                    await emailService.SendAsync(recipient.Email, $"Arıza Yorumu — {mr.Title}", html, ct);
                }
            }
        }
        else
        {
            // Durum degisikligi → bildiren sakine email
            var recipient = await conn.QuerySingleOrDefaultAsync<EmailRecipient>(
                """
                SELECT p.full_name, au.email
                FROM public.profiles p
                JOIN auth.users au ON au.id = p.id
                WHERE p.id = @Id AND p.notification_email = true
                """,
                new { Id = mr.ReportedBy });

            if (recipient is not null && !string.IsNullOrEmpty(recipient.Email))
            {
                var html = EmailTemplates.MaintenanceRequestStatusChanged(
                    mr.OrgName, recipient.FullName, mr.Title, newStatus, note);
                await emailService.SendAsync(recipient.Email, $"Arıza Durumu — {mr.Title}", html, ct);
            }
        }

        _logger.LogInformation("maintenance_status_email: {MrId} — status={Status}", mrId, newStatus);
    }

    private async Task ProcessMaintenanceSlaEmailAsync(IDbConnectionFactory factory, IEmailService emailService, JobRow job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(job.Payload);
        var mrId = payload.GetProperty("maintenance_request_id").GetGuid();
        var orgId = payload.GetProperty("organization_id").GetGuid();

        using var conn = factory.CreateServiceRoleConnection();

        var mr = await conn.QuerySingleOrDefaultAsync<MaintenanceSlaInfo>(
            """
            SELECT mr.title, mr.category, mr.priority, mr.sla_deadline_at,
                   o.name AS org_name, p.full_name AS reported_by_name
            FROM public.maintenance_requests mr
            JOIN public.organizations o ON o.id = mr.organization_id
            JOIN public.profiles p ON p.id = mr.reported_by
            WHERE mr.id = @Id AND mr.deleted_at IS NULL
            """,
            new { Id = mrId });

        if (mr is null)
        {
            _logger.LogWarning("maintenance_sla_email: {MrId} ariza bulunamadi.", mrId);
            return;
        }

        var admins = (await conn.QueryAsync<EmailRecipient>(
            """
            SELECT DISTINCT p.full_name, au.email
            FROM public.organization_members om
            JOIN public.profiles p ON p.id = om.user_id
            JOIN auth.users au ON au.id = om.user_id
            WHERE om.organization_id = @OrgId AND om.role = 'admin' AND om.status = 'active'
              AND p.notification_email = true
            """,
            new { OrgId = orgId })).ToList();

        var slaDeadline = mr.SlaDeadlineAt.HasValue
            ? new DateTimeOffset(mr.SlaDeadlineAt.Value, TimeSpan.Zero)
            : DateTimeOffset.UtcNow;

        foreach (var r in admins)
        {
            if (string.IsNullOrEmpty(r.Email)) continue;
            var html = EmailTemplates.MaintenanceRequestSlaBreached(
                mr.OrgName, r.FullName, mr.Title, mr.Category, mr.Priority,
                mr.ReportedByName, slaDeadline);
            await emailService.SendAsync(r.Email, $"SLA Aşıldı — {mr.Title}", html, ct);
        }

        // In-app notification (admin user_id'leri ayrıca al)
        var adminUserIds = await conn.QueryAsync<Guid>(
            """
            SELECT user_id FROM public.organization_members
            WHERE organization_id = @OrgId AND role = 'admin' AND status = 'active'
            """,
            new { OrgId = orgId });

        foreach (var adminUserId in adminUserIds)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO public.notifications
                    (id, organization_id, user_id, type, title, body, reference_type, reference_id, created_at)
                VALUES (@Id, @OrgId, @UserId, 'maintenance_sla_breached', @Title, @Body,
                        'maintenance_request', @RefId, now())
                """,
                new
                {
                    Id = Guid.NewGuid(), OrgId = orgId, UserId = adminUserId,
                    Title = $"SLA Aşıldı: {mr.Title}",
                    Body = $"{mr.Category} — SLA süresi doldu",
                    RefId = mrId
                });
        }

        _logger.LogInformation("maintenance_sla_email: {MrId} — {Count} admin'e email gönderildi.", mrId, admins.Count);
    }

    private record JobRow(Guid Id, string JobType, string Payload);
    private record ReminderRecipient(string FullName, string? Email, decimal TotalOwed);
    private record EmailRecipient(string FullName, string? Email);
    private record PeriodInfo(string PeriodName, string OrgName);
    private record LateNoticeDetail(decimal Amount, DateTime DueDate, string PeriodName, string OrgName);
    private record PaymentEmailDetail(string ReceiptNumber, decimal Amount, string PeriodName, string OrgName);
    private record FinanceTotalsRow(decimal TotalIncome, decimal TotalExpense);
    private record TopCategoryRow(string Name, decimal Amount);
    private record AnnouncementEmailInfo(string Title, string Body, string Category, string TargetType, string? TargetIds, string OrgName, string AuthorName);
    private record MaintenanceEmailInfo(string Title, string Category, string Priority, Guid ReportedBy, string LocationType, string? LocationNote, string OrgName, string ReportedByName);
    private record MaintenanceSlaInfo(string Title, string Category, string Priority, DateTime? SlaDeadlineAt, string OrgName, string ReportedByName);
}
