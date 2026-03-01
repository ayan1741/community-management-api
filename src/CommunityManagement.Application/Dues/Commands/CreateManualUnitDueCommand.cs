using CommunityManagement.Application.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Dues.Commands;

public record CreateManualUnitDueCommand(
    Guid OrgId,
    Guid PeriodId,
    Guid UnitId,
    Guid DueTypeId,
    decimal Amount,
    string? Note
) : IRequest<UnitDue>;

public class CreateManualUnitDueCommandHandler : IRequestHandler<CreateManualUnitDueCommand, UnitDue>
{
    private readonly IDuesPeriodRepository _periods;
    private readonly IDueTypeRepository _dueTypes;
    private readonly IUnitDueRepository _unitDues;
    private readonly IUnitRepository _units;
    private readonly ICurrentUserService _currentUser;

    public CreateManualUnitDueCommandHandler(
        IDuesPeriodRepository periods,
        IDueTypeRepository dueTypes,
        IUnitDueRepository unitDues,
        IUnitRepository units,
        ICurrentUserService currentUser)
    {
        _periods = periods;
        _dueTypes = dueTypes;
        _unitDues = unitDues;
        _units = units;
        _currentUser = currentUser;
    }

    public async Task<UnitDue> Handle(CreateManualUnitDueCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var period = await _periods.GetByIdAsync(request.PeriodId, ct)
            ?? throw AppException.NotFound("Dönem bulunamadı.");

        if (period.OrganizationId != request.OrgId)
            throw AppException.NotFound("Dönem bulunamadı.");

        if (period.Status is "closed" or "processing")
            throw AppException.UnprocessableEntity("Kapalı veya işlemde olan döneme tahakkuk eklenemez.");

        var unit = await _units.GetByIdAsync(request.UnitId, ct)
            ?? throw AppException.NotFound("Daire bulunamadı.");

        if (unit.OrganizationId != request.OrgId)
            throw AppException.NotFound("Daire bulunamadı.");

        var dueType = await _dueTypes.GetByIdAsync(request.DueTypeId, ct)
            ?? throw AppException.NotFound("Aidat tipi bulunamadı.");

        if (dueType.OrganizationId != request.OrgId)
            throw AppException.NotFound("Aidat tipi bulunamadı.");

        if (!dueType.IsActive)
            throw AppException.UnprocessableEntity("Pasif aidat tipine tahakkuk oluşturulamaz.");

        var exists = await _unitDues.ExistsAsync(request.PeriodId, request.UnitId, request.DueTypeId, ct);
        if (exists)
            throw AppException.Conflict("Bu dönemde bu daire için aynı aidat tipinde tahakkuk zaten mevcut.");

        var unitDue = new UnitDue
        {
            Id = Guid.NewGuid(),
            PeriodId = request.PeriodId,
            UnitId = request.UnitId,
            DueTypeId = request.DueTypeId,
            Amount = request.Amount,
            Status = "pending",
            CreatedBy = _currentUser.UserId,
            Note = request.Note,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        return await _unitDues.CreateAsync(unitDue, ct);
    }
}
