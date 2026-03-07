using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Polls.Queries;

public record GetPollsQuery(
    Guid OrgId, string? Status, int Page, int PageSize
) : IRequest<GetPollsResult>;

public record GetPollsResult(
    IReadOnlyList<PollListDto> Items, int TotalCount,
    int Page, int PageSize);

public class GetPollsQueryHandler : IRequestHandler<GetPollsQuery, GetPollsResult>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IPollRepository _repo;

    public GetPollsQueryHandler(ICurrentUserService currentUser, IPollRepository repo)
    {
        _currentUser = currentUser;
        _repo = repo;
    }

    public async Task<GetPollsResult> Handle(GetPollsQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var (items, totalCount) = await _repo.GetListAsync(
            request.OrgId, _currentUser.UserId,
            request.Status, request.Page, request.PageSize, ct);

        return new GetPollsResult(items, totalCount, request.Page, request.PageSize);
    }
}
