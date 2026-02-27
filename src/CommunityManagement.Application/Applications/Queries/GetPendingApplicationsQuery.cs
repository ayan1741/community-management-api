using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Applications.Queries;

public record GetPendingApplicationsQuery(
    Guid OrgId,
    ApplicationStatus? Status,
    int Page,
    int PageSize
) : IRequest<GetApplicationsResult>;

public record GetApplicationsResult(
    IReadOnlyList<ApplicationListItem> Items,
    int TotalCount,
    int Page,
    int PageSize
);

public class GetPendingApplicationsQueryHandler : IRequestHandler<GetPendingApplicationsQuery, GetApplicationsResult>
{
    private readonly IApplicationRepository _applications;
    private readonly ICurrentUserService _currentUser;

    public GetPendingApplicationsQueryHandler(IApplicationRepository applications, ICurrentUserService currentUser)
    {
        _applications = applications;
        _currentUser = currentUser;
    }

    public async Task<GetApplicationsResult> Handle(GetPendingApplicationsQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var memberStatus = await _currentUser.GetMembershipStatusAsync(request.OrgId, ct);
        if (memberStatus == MemberStatus.Suspended)
            throw AppException.Forbidden("Hesabınız bu organizasyonda askıya alınmış.");

        var (items, totalCount) = await _applications.GetByOrgIdAsync(
            request.OrgId, request.Status ?? ApplicationStatus.Pending,
            request.Page, request.PageSize, ct);

        return new GetApplicationsResult(items, totalCount, request.Page, request.PageSize);
    }
}
