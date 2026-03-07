using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.MaintenanceRequests.Commands;

public record CreateMaintenanceRequestCommand(
    Guid OrgId, string Title, string Description,
    string Category, string Priority,
    string LocationType, Guid? UnitId, string? LocationNote
) : IRequest<MaintenanceRequest>;

public class CreateMaintenanceRequestCommandHandler : IRequestHandler<CreateMaintenanceRequestCommand, MaintenanceRequest>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IMaintenanceRequestRepository _repo;

    private static readonly Dictionary<string, TimeSpan> SlaDurations = new()
    {
        ["asansor"]         = TimeSpan.FromHours(4),
        ["su_tesisati"]     = TimeSpan.FromHours(24),
        ["elektrik"]        = TimeSpan.FromHours(24),
        ["guvenlik"]        = TimeSpan.FromHours(4),
        ["isitma_sogutma"]  = TimeSpan.FromHours(48),
        ["ortak_alan"]      = TimeSpan.FromHours(72),
        ["boya_badana"]     = TimeSpan.FromDays(7),
        ["diger"]           = TimeSpan.FromHours(72),
    };

    public CreateMaintenanceRequestCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IMaintenanceRequestRepository repo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
    }

    public async Task<MaintenanceRequest> Handle(CreateMaintenanceRequestCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        // Validasyon
        if (string.IsNullOrWhiteSpace(request.Title))
            throw AppException.UnprocessableEntity("Baslik zorunludur.");
        if (request.Title.Trim().Length > 200)
            throw AppException.UnprocessableEntity("Baslik en fazla 200 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(request.Description))
            throw AppException.UnprocessableEntity("Aciklama zorunludur.");

        if (request.Category is not ("elektrik" or "su_tesisati" or "asansor" or "ortak_alan"
            or "boya_badana" or "isitma_sogutma" or "guvenlik" or "diger"))
            throw AppException.UnprocessableEntity("Gecersiz kategori.");
        if (request.Priority is not ("dusuk" or "normal" or "yuksek" or "acil"))
            throw AppException.UnprocessableEntity("Gecersiz oncelik.");
        if (request.LocationType is not ("unit" or "common_area"))
            throw AppException.UnprocessableEntity("Gecersiz konum tipi.");

        var currentUserId = _currentUser.UserId;
        var role = await _currentUser.GetRoleAsync(request.OrgId, ct);

        // Sakin sadece kendi dairesini secebilir
        if (request.LocationType == "unit" && request.UnitId.HasValue && role == MemberRole.Resident)
        {
            using var checkConn = _factory.CreateUserConnection();
            var isOwner = await checkConn.QuerySingleAsync<long>(
                """
                SELECT COUNT(*) FROM public.unit_residents
                WHERE unit_id = @UnitId AND user_id = @UserId AND status = 'active'
                """,
                new { request.UnitId, UserId = currentUserId });
            if (isOwner == 0)
                throw AppException.Forbidden("Sadece kendi daireniz icin ariza bildirebilirsiniz.");
        }

        // SLA hesapla
        var sla = SlaDurations.GetValueOrDefault(request.Category, TimeSpan.FromHours(72));
        if (request.Priority == "acil") sla = TimeSpan.FromTicks(sla.Ticks / 2);
        var now = DateTimeOffset.UtcNow;
        var slaDeadlineAt = now + sla;

        // Tekrarlayan ariza kontrolu
        var recentCount = await _repo.CountRecentByUnitAndCategoryAsync(
            request.OrgId, request.UnitId, request.Category, 90, ct);
        var isRecurring = recentCount >= 1;

        var entity = new MaintenanceRequest
        {
            Id = Guid.NewGuid(),
            OrganizationId = request.OrgId,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            Category = request.Category,
            Priority = request.Priority,
            Status = "reported",
            LocationType = request.LocationType,
            UnitId = request.UnitId,
            LocationNote = request.LocationNote?.Trim(),
            IsRecurring = isRecurring,
            SlaDeadlineAt = slaDeadlineAt,
            ReportedBy = currentUserId,
            CreatedBy = currentUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            // 1. Ariza kaydi
            await conn.ExecuteAsync(
                """
                INSERT INTO public.maintenance_requests
                    (id, organization_id, title, description, category, priority, status,
                     location_type, unit_id, location_note, is_recurring,
                     sla_deadline_at, reported_by, created_by, created_at, updated_at)
                VALUES
                    (@Id, @OrganizationId, @Title, @Description, @Category, @Priority, @Status,
                     @LocationType, @UnitId, @LocationNote, @IsRecurring,
                     @SlaDeadlineAt, @ReportedBy, @CreatedBy, @CreatedAt, @UpdatedAt)
                """,
                new
                {
                    entity.Id, entity.OrganizationId, entity.Title, entity.Description,
                    entity.Category, entity.Priority, entity.Status,
                    entity.LocationType, entity.UnitId, entity.LocationNote, entity.IsRecurring,
                    SlaDeadlineAt = entity.SlaDeadlineAt?.UtcDateTime,
                    entity.ReportedBy, entity.CreatedBy,
                    CreatedAt = entity.CreatedAt.UtcDateTime,
                    UpdatedAt = entity.UpdatedAt.UtcDateTime
                }, tx);

            // 2. Ilk log kaydi (reported)
            await conn.ExecuteAsync(
                """
                INSERT INTO public.maintenance_request_logs
                    (id, maintenance_request_id, from_status, to_status, note, created_by, created_at)
                VALUES (@Id, @MrId, NULL, 'reported', 'Ariza bildirimi olusturuldu', @UserId, @Now)
                """,
                new { Id = Guid.NewGuid(), MrId = entity.Id, UserId = currentUserId, Now = now.UtcDateTime }, tx);

            // 3. Admin'lere bildirim
            var admins = await conn.QueryAsync<Guid>(
                """
                SELECT user_id FROM public.organization_members
                WHERE organization_id = @OrgId AND role = 'admin' AND status = 'active'
                """,
                new { request.OrgId }, tx);

            foreach (var adminId in admins)
            {
                await conn.ExecuteAsync(
                    """
                    INSERT INTO public.notifications
                        (id, organization_id, user_id, type, title, body, reference_type, reference_id, created_at)
                    VALUES (@Id, @OrgId, @UserId, 'maintenance_created', @Title, @Body,
                            'maintenance_request', @RefId, @Now)
                    """,
                    new
                    {
                        Id = Guid.NewGuid(), request.OrgId, UserId = adminId,
                        Title = $"Yeni Ariza: {entity.Title}",
                        Body = $"{entity.Category} — {entity.Priority}",
                        RefId = entity.Id, Now = now.UtcDateTime
                    }, tx);
            }

            // 4. Email job (admin'lere)
            await conn.ExecuteAsync(
                """
                INSERT INTO public.background_jobs (job_type, payload, status, created_at)
                VALUES ('maintenance_status_email', @Payload::jsonb, 'queued', @Now)
                """,
                new
                {
                    Payload = JsonSerializer.Serialize(new
                    {
                        MaintenanceRequestId = entity.Id,
                        OrganizationId = request.OrgId,
                        NewStatus = "reported"
                    }),
                    Now = now.UtcDateTime
                }, tx);

            // 5. Audit log
            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (organization_id, table_name, record_id, actor_id, action, new_values)
                VALUES (@OrgId, 'maintenance_requests', @RecordId, @ActorId, 'insert', @NewValues::jsonb)
                """,
                new
                {
                    OrgId = request.OrgId,
                    RecordId = entity.Id, ActorId = currentUserId,
                    NewValues = JsonSerializer.Serialize(new
                    {
                        entity.Title, entity.Category, entity.Priority, entity.Status
                    })
                }, tx);

            await tx.CommitAsync(ct);
            return entity;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
