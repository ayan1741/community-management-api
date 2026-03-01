using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Dues.Commands;

public record DeleteDuesPeriodCommand(Guid OrgId, Guid PeriodId) : IRequest;

public class DeleteDuesPeriodCommandHandler : IRequestHandler<DeleteDuesPeriodCommand>
{
    private readonly IDuesPeriodRepository _periods;
    private readonly ICurrentUserService _currentUser;

    public DeleteDuesPeriodCommandHandler(IDuesPeriodRepository periods, ICurrentUserService currentUser)
    {
        _periods = periods;
        _currentUser = currentUser;
    }

    public async Task Handle(DeleteDuesPeriodCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var period = await _periods.GetByIdAsync(request.PeriodId, ct)
            ?? throw AppException.NotFound("Dönem bulunamadı.");

        if (period.OrganizationId != request.OrgId)
            throw AppException.NotFound("Dönem bulunamadı.");

        if (period.Status != "draft")
            throw AppException.UnprocessableEntity("Yalnızca taslak (draft) dönemler silinebilir.");

        var hasAccruals = await _periods.HasAccrualsAsync(request.PeriodId, ct);
        if (hasAccruals)
            throw AppException.UnprocessableEntity("Tahakkuku bulunan dönem silinemez. Önce tahakkukları iptal edin.");

        await _periods.DeleteAsync(request.PeriodId, ct);
    }
}
