using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Agenda.Queries;

public record GetAgendaCommentsQuery(
    Guid OrgId, Guid AgendaItemId, int Page, int PageSize
) : IRequest<GetAgendaCommentsResult>;

public record GetAgendaCommentsResult(
    IReadOnlyList<AgendaCommentDto> Items, int TotalCount,
    int Page, int PageSize);

public class GetAgendaCommentsQueryHandler : IRequestHandler<GetAgendaCommentsQuery, GetAgendaCommentsResult>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IAgendaRepository _repo;

    public GetAgendaCommentsQueryHandler(ICurrentUserService currentUser, IAgendaRepository repo)
    {
        _currentUser = currentUser;
        _repo = repo;
    }

    public async Task<GetAgendaCommentsResult> Handle(GetAgendaCommentsQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var entity = await _repo.GetByIdAsync(request.AgendaItemId, ct)
            ?? throw AppException.NotFound("Gundem maddesi bulunamadi.");
        if (entity.OrganizationId != request.OrgId)
            throw AppException.NotFound("Gundem maddesi bulunamadi.");

        var (items, totalCount) = await _repo.GetCommentsAsync(
            request.AgendaItemId, request.Page, request.PageSize, ct);

        return new GetAgendaCommentsResult(items, totalCount, request.Page, request.PageSize);
    }
}
