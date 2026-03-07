using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Polls.Queries;

public record GetPollDetailQuery(Guid OrgId, Guid PollId) : IRequest<GetPollDetailResult>;

public record GetPollDetailResult(
    PollDetailDto Poll,
    IReadOnlyList<PollOptionDto> Options,
    UserVoteDto? UserVote);

public class GetPollDetailQueryHandler : IRequestHandler<GetPollDetailQuery, GetPollDetailResult>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IPollRepository _repo;

    public GetPollDetailQueryHandler(ICurrentUserService currentUser, IPollRepository repo)
    {
        _currentUser = currentUser;
        _repo = repo;
    }

    public async Task<GetPollDetailResult> Handle(GetPollDetailQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var detail = await _repo.GetDetailAsync(request.PollId, ct)
            ?? throw AppException.NotFound("Oylama bulunamadi.");
        if (detail.OrganizationId != request.OrgId)
            throw AppException.NotFound("Oylama bulunamadi.");

        var options = await _repo.GetOptionsAsync(request.PollId, ct);
        var userVote = await _repo.GetUserVoteAsync(request.PollId, _currentUser.UserId, ct);

        return new GetPollDetailResult(detail, options, userVote);
    }
}
