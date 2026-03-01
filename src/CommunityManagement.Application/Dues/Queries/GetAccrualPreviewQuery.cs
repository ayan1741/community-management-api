using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Dues.Queries;

public record GetAccrualPreviewQuery(
    Guid OrgId,
    Guid PeriodId,
    IReadOnlyList<Guid> DueTypeIds,
    bool IncludeEmptyUnits
) : IRequest<AccrualPreview>;

public class GetAccrualPreviewQueryHandler : IRequestHandler<GetAccrualPreviewQuery, AccrualPreview>
{
    private readonly IDuesPeriodRepository _periods;
    private readonly IUnitDueRepository _unitDues;
    private readonly ICurrentUserService _currentUser;

    public GetAccrualPreviewQueryHandler(
        IDuesPeriodRepository periods,
        IUnitDueRepository unitDues,
        ICurrentUserService currentUser)
    {
        _periods = periods;
        _unitDues = unitDues;
        _currentUser = currentUser;
    }

    public async Task<AccrualPreview> Handle(GetAccrualPreviewQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var period = await _periods.GetByIdAsync(request.PeriodId, ct)
            ?? throw AppException.NotFound("Dönem bulunamadı.");

        if (period.OrganizationId != request.OrgId)
            throw AppException.NotFound("Dönem bulunamadı.");

        var parameters = new AccrualParams(
            request.PeriodId, request.OrgId,
            request.DueTypeIds, request.IncludeEmptyUnits,
            _currentUser.UserId);

        return await _unitDues.GetAccrualPreviewAsync(parameters, ct);
    }
}
