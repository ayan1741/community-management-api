using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Dues.Commands;

public record CloseDuesPeriodCommand(Guid OrgId, Guid PeriodId) : IRequest;

public class CloseDuesPeriodCommandHandler : IRequestHandler<CloseDuesPeriodCommand>
{
    private readonly IDuesPeriodRepository _periods;
    private readonly ICurrentUserService _currentUser;

    public CloseDuesPeriodCommandHandler(IDuesPeriodRepository periods, ICurrentUserService currentUser)
    {
        _periods = periods;
        _currentUser = currentUser;
    }

    public async Task Handle(CloseDuesPeriodCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var period = await _periods.GetByIdAsync(request.PeriodId, ct)
            ?? throw AppException.NotFound("Dönem bulunamadı.");

        if (period.OrganizationId != request.OrgId)
            throw AppException.NotFound("Dönem bulunamadı.");

        if (period.Status == "closed")
            throw AppException.UnprocessableEntity("Dönem zaten kapalı.");

        if (period.Status is "draft" or "processing")
            throw AppException.UnprocessableEntity("Yalnızca aktif dönemler kapatılabilir.");

        await _periods.UpdateStatusAsync(request.PeriodId, "closed", DateTimeOffset.UtcNow, ct);
    }
}
