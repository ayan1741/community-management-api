using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Meetings.Queries;

public record GetMeetingsQuery(
    Guid OrgId, string? Status, int Page, int PageSize
) : IRequest<GetMeetingsResult>;

public record GetMeetingsResult(
    IReadOnlyList<MeetingListDto> Items, int TotalCount,
    int Page, int PageSize);

public class GetMeetingsQueryHandler : IRequestHandler<GetMeetingsQuery, GetMeetingsResult>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IMeetingRepository _repo;

    public GetMeetingsQueryHandler(ICurrentUserService currentUser, IMeetingRepository repo)
    {
        _currentUser = currentUser;
        _repo = repo;
    }

    public async Task<GetMeetingsResult> Handle(GetMeetingsQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var (items, totalCount) = await _repo.GetListAsync(
            request.OrgId, request.Status,
            request.Page, request.PageSize, ct);

        return new GetMeetingsResult(items, totalCount, request.Page, request.PageSize);
    }
}
