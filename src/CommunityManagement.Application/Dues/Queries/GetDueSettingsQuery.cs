using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Dues.Queries;

public record GetDueSettingsQuery(Guid OrgId) : IRequest<OrganizationDueSettings>;

public class GetDueSettingsQueryHandler : IRequestHandler<GetDueSettingsQuery, OrganizationDueSettings>
{
    private readonly IOrganizationDueSettingsRepository _settings;
    private readonly ICurrentUserService _currentUser;

    public GetDueSettingsQueryHandler(IOrganizationDueSettingsRepository settings, ICurrentUserService currentUser)
    {
        _settings = settings;
        _currentUser = currentUser;
    }

    public async Task<OrganizationDueSettings> Handle(GetDueSettingsQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);
        return await _settings.GetOrDefaultAsync(request.OrgId, ct);
    }
}
