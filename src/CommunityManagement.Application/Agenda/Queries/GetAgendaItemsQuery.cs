using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Agenda.Queries;

public record GetAgendaItemsQuery(
    Guid OrgId, string? Status, string? Category, Guid? MeetingId,
    string SortBy, string SortDirection,
    int Page, int PageSize
) : IRequest<GetAgendaItemsResult>;

public record GetAgendaItemsResult(
    IReadOnlyList<AgendaItemListDto> Items, int TotalCount,
    int Page, int PageSize);

public class GetAgendaItemsQueryHandler : IRequestHandler<GetAgendaItemsQuery, GetAgendaItemsResult>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IAgendaRepository _repo;

    public GetAgendaItemsQueryHandler(ICurrentUserService currentUser, IAgendaRepository repo)
    {
        _currentUser = currentUser;
        _repo = repo;
    }

    public async Task<GetAgendaItemsResult> Handle(GetAgendaItemsQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var (items, totalCount) = await _repo.GetListAsync(
            request.OrgId, _currentUser.UserId,
            request.Status, request.Category, request.MeetingId,
            request.SortBy, request.SortDirection,
            request.Page, request.PageSize, ct);

        return new GetAgendaItemsResult(items, totalCount, request.Page, request.PageSize);
    }
}
