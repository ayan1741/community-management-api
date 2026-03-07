using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Agenda.Queries;

public record GetAgendaStatsQuery(Guid OrgId) : IRequest<AgendaStats>;

public class GetAgendaStatsQueryHandler : IRequestHandler<GetAgendaStatsQuery, AgendaStats>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IAgendaRepository _repo;

    public GetAgendaStatsQueryHandler(ICurrentUserService currentUser, IAgendaRepository repo)
    {
        _currentUser = currentUser;
        _repo = repo;
    }

    public async Task<AgendaStats> Handle(GetAgendaStatsQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);
        return await _repo.GetStatsAsync(request.OrgId, ct);
    }
}
