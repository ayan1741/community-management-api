using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Decisions.Queries;

public record GetDecisionsQuery(
    Guid OrgId, string? Status,
    DateTimeOffset? FromDate, DateTimeOffset? ToDate,
    int Page, int PageSize
) : IRequest<GetDecisionsResult>;

public record GetDecisionsResult(
    IReadOnlyList<DecisionListDto> Items, int TotalCount,
    int Page, int PageSize);

public class GetDecisionsQueryHandler : IRequestHandler<GetDecisionsQuery, GetDecisionsResult>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDecisionRepository _repo;

    public GetDecisionsQueryHandler(ICurrentUserService currentUser, IDecisionRepository repo)
    {
        _currentUser = currentUser;
        _repo = repo;
    }

    public async Task<GetDecisionsResult> Handle(GetDecisionsQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var (items, totalCount) = await _repo.GetListAsync(
            request.OrgId, request.Status,
            request.FromDate, request.ToDate,
            request.Page, request.PageSize, ct);

        return new GetDecisionsResult(items, totalCount, request.Page, request.PageSize);
    }
}
