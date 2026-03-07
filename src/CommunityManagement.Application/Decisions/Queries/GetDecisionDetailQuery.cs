using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Decisions.Queries;

public record GetDecisionDetailQuery(Guid OrgId, Guid Id) : IRequest<DecisionDetailDto>;

public class GetDecisionDetailQueryHandler : IRequestHandler<GetDecisionDetailQuery, DecisionDetailDto>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDecisionRepository _repo;

    public GetDecisionDetailQueryHandler(ICurrentUserService currentUser, IDecisionRepository repo)
    {
        _currentUser = currentUser;
        _repo = repo;
    }

    public async Task<DecisionDetailDto> Handle(GetDecisionDetailQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var detail = await _repo.GetDetailAsync(request.Id, ct)
            ?? throw AppException.NotFound("Karar bulunamadi.");
        if (detail.OrganizationId != request.OrgId)
            throw AppException.NotFound("Karar bulunamadi.");

        return detail;
    }
}
