using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Agenda.Queries;

public record GetAgendaItemDetailQuery(Guid OrgId, Guid Id) : IRequest<AgendaItemDetailDto>;

public class GetAgendaItemDetailQueryHandler : IRequestHandler<GetAgendaItemDetailQuery, AgendaItemDetailDto>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IAgendaRepository _repo;

    public GetAgendaItemDetailQueryHandler(ICurrentUserService currentUser, IAgendaRepository repo)
    {
        _currentUser = currentUser;
        _repo = repo;
    }

    public async Task<AgendaItemDetailDto> Handle(GetAgendaItemDetailQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var detail = await _repo.GetDetailAsync(request.Id, _currentUser.UserId, ct)
            ?? throw AppException.NotFound("Gundem maddesi bulunamadi.");
        if (detail.OrganizationId != request.OrgId)
            throw AppException.NotFound("Gundem maddesi bulunamadi.");

        return detail;
    }
}
