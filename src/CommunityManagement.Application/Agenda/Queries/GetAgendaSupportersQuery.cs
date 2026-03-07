using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Agenda.Queries;

public record GetAgendaSupportersQuery(Guid OrgId, Guid AgendaItemId) : IRequest<IReadOnlyList<AgendaSupportDetailDto>>;

public class GetAgendaSupportersQueryHandler : IRequestHandler<GetAgendaSupportersQuery, IReadOnlyList<AgendaSupportDetailDto>>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IAgendaRepository _repo;

    public GetAgendaSupportersQueryHandler(ICurrentUserService currentUser, IAgendaRepository repo)
    {
        _currentUser = currentUser;
        _repo = repo;
    }

    public async Task<IReadOnlyList<AgendaSupportDetailDto>> Handle(GetAgendaSupportersQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var entity = await _repo.GetByIdAsync(request.AgendaItemId, ct)
            ?? throw AppException.NotFound("Gundem maddesi bulunamadi.");
        if (entity.OrganizationId != request.OrgId)
            throw AppException.NotFound("Gundem maddesi bulunamadi.");

        return await _repo.GetSupportersAsync(request.AgendaItemId, ct);
    }
}
