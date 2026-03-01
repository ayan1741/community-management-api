using CommunityManagement.Application.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Dues.Queries;

public record GetLateFeesByUnitDueQuery(Guid OrgId, Guid UnitDueId) : IRequest<IReadOnlyList<LateFee>>;

public class GetLateFeesByUnitDueQueryHandler : IRequestHandler<GetLateFeesByUnitDueQuery, IReadOnlyList<LateFee>>
{
    private readonly ILateFeeRepository _lateFees;
    private readonly IUnitDueRepository _unitDues;
    private readonly IDuesPeriodRepository _periods;
    private readonly ICurrentUserService _currentUser;

    public GetLateFeesByUnitDueQueryHandler(
        ILateFeeRepository lateFees,
        IUnitDueRepository unitDues,
        IDuesPeriodRepository periods,
        ICurrentUserService currentUser)
    {
        _lateFees = lateFees;
        _unitDues = unitDues;
        _periods = periods;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<LateFee>> Handle(GetLateFeesByUnitDueQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.BoardMember, ct);

        var unitDue = await _unitDues.GetByIdAsync(request.UnitDueId, ct)
            ?? throw AppException.NotFound("Tahakkuk bulunamadı.");

        var period = await _periods.GetByIdAsync(unitDue.PeriodId, ct)
            ?? throw AppException.NotFound("Dönem bulunamadı.");

        if (period.OrganizationId != request.OrgId)
            throw AppException.NotFound("Tahakkuk bulunamadı.");

        return await _lateFees.GetByUnitDueIdAsync(request.UnitDueId, ct);
    }
}
