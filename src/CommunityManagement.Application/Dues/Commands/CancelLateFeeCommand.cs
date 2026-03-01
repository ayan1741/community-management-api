using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Dues.Commands;

public record CancelLateFeeCommand(Guid OrgId, Guid UnitDueId, Guid LateFeeId, string Note) : IRequest;

public class CancelLateFeeCommandHandler : IRequestHandler<CancelLateFeeCommand>
{
    private readonly IUnitDueRepository _unitDues;
    private readonly IDuesPeriodRepository _periods;
    private readonly ILateFeeRepository _lateFees;
    private readonly ICurrentUserService _currentUser;

    public CancelLateFeeCommandHandler(
        IUnitDueRepository unitDues,
        IDuesPeriodRepository periods,
        ILateFeeRepository lateFees,
        ICurrentUserService currentUser)
    {
        _unitDues = unitDues;
        _periods = periods;
        _lateFees = lateFees;
        _currentUser = currentUser;
    }

    public async Task Handle(CancelLateFeeCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var unitDue = await _unitDues.GetByIdAsync(request.UnitDueId, ct)
            ?? throw AppException.NotFound("Tahakkuk bulunamadı.");

        var period = await _periods.GetByIdAsync(unitDue.PeriodId, ct)
            ?? throw AppException.NotFound("Dönem bulunamadı.");

        if (period.OrganizationId != request.OrgId)
            throw AppException.NotFound("Tahakkuk bulunamadı.");

        var lateFees = await _lateFees.GetByUnitDueIdAsync(request.UnitDueId, ct);
        var lateFee = lateFees.FirstOrDefault(f => f.Id == request.LateFeeId)
            ?? throw AppException.NotFound("Gecikme zammı bulunamadı.");

        if (lateFee.Status == "cancelled")
            throw AppException.UnprocessableEntity("Gecikme zammı zaten iptal edilmiş.");

        await _lateFees.CancelAsync(request.LateFeeId, _currentUser.UserId, request.Note, ct);
    }
}
