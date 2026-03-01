using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Data.Common;
using System.Text.Json;

namespace CommunityManagement.Application.Dues.Commands;

public record TriggerBulkAccrualCommand(
    Guid OrgId,
    Guid PeriodId,
    IReadOnlyList<Guid> DueTypeIds,
    bool IncludeEmptyUnits,
    bool Confirmed
) : IRequest<BulkAccrualResult>;

public record BulkAccrualResult(AccrualPreview Preview, Guid? JobId);

public class TriggerBulkAccrualCommandHandler : IRequestHandler<TriggerBulkAccrualCommand, BulkAccrualResult>
{
    private readonly IDuesPeriodRepository _periods;
    private readonly IDueTypeRepository _dueTypes;
    private readonly IUnitDueRepository _unitDues;
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;

    public TriggerBulkAccrualCommandHandler(
        IDuesPeriodRepository periods,
        IDueTypeRepository dueTypes,
        IUnitDueRepository unitDues,
        ICurrentUserService currentUser,
        IDbConnectionFactory factory)
    {
        _periods = periods;
        _dueTypes = dueTypes;
        _unitDues = unitDues;
        _currentUser = currentUser;
        _factory = factory;
    }

    public async Task<BulkAccrualResult> Handle(TriggerBulkAccrualCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        if (request.DueTypeIds.Count == 0)
            throw AppException.UnprocessableEntity("En az bir aidat tipi seçilmelidir.");

        var period = await _periods.GetByIdAsync(request.PeriodId, ct)
            ?? throw AppException.NotFound("Dönem bulunamadı.");

        if (period.OrganizationId != request.OrgId)
            throw AppException.NotFound("Dönem bulunamadı.");

        if (period.Status is not ("draft" or "failed"))
            throw AppException.UnprocessableEntity("Toplu tahakkuk yalnızca taslak veya başarısız dönemler için tetiklenebilir.");

        var hasAccruals = await _periods.HasAccrualsAsync(request.PeriodId, ct);
        if (hasAccruals)
            throw AppException.UnprocessableEntity("Bu dönemde zaten iptal edilmemiş tahakkuklar mevcut.");

        // Aidat tiplerini doğrula
        foreach (var typeId in request.DueTypeIds)
        {
            var dt = await _dueTypes.GetByIdAsync(typeId, ct)
                ?? throw AppException.NotFound($"Aidat tipi bulunamadı: {typeId}");

            if (dt.OrganizationId != request.OrgId)
                throw AppException.NotFound($"Aidat tipi bulunamadı: {typeId}");

            if (!dt.IsActive)
                throw AppException.UnprocessableEntity($"'{dt.Name}' aidat tipi pasif — aktif bir tip seçin.");
        }

        var parameters = new AccrualParams(
            request.PeriodId, request.OrgId,
            request.DueTypeIds, request.IncludeEmptyUnits,
            _currentUser.UserId);

        var preview = await _unitDues.GetAccrualPreviewAsync(parameters, ct);

        if (!request.Confirmed)
            return new BulkAccrualResult(preview, null);

        // [UYARI-5]: status güncelleme + job insert aynı transaction'da
        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            // Period → 'processing' (optimistic lock: status kontrolü WHERE'de)
            var affected = await conn.ExecuteAsync(
                "UPDATE public.dues_periods SET status = 'processing', updated_at = now() WHERE id = @Id AND status IN ('draft', 'failed')",
                new { Id = request.PeriodId }, tx);

            if (affected == 0)
                throw AppException.UnprocessableEntity("Dönem başka bir istek tarafından işleme alındı — lütfen sayfayı yenileyip tekrar deneyin.");

            // Background job INSERT
            var payload = JsonSerializer.Serialize(new
            {
                PeriodId = request.PeriodId,
                OrgId = request.OrgId,
                DueTypeIds = request.DueTypeIds,
                IncludeEmptyUnits = request.IncludeEmptyUnits,
                CreatedBy = _currentUser.UserId
            });

            var jobId = await conn.QuerySingleAsync<Guid>(
                """
                INSERT INTO public.background_jobs (job_type, payload, status)
                VALUES ('bulk_accrual', @Payload::jsonb, 'queued')
                RETURNING id
                """,
                new { Payload = payload }, tx);

            await tx.CommitAsync(ct);
            return new BulkAccrualResult(preview, jobId);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
