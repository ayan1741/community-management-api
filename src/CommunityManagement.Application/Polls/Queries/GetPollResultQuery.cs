using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Polls.Queries;

public record GetPollResultQuery(Guid OrgId, Guid PollId) : IRequest<PollResultDto>;

public class GetPollResultQueryHandler : IRequestHandler<GetPollResultQuery, PollResultDto>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IPollRepository _repo;

    public GetPollResultQueryHandler(ICurrentUserService currentUser, IPollRepository repo)
    {
        _currentUser = currentUser;
        _repo = repo;
    }

    public async Task<PollResultDto> Handle(GetPollResultQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var result = await _repo.GetResultAsync(request.PollId, ct)
            ?? throw AppException.NotFound("Oylama bulunamadi.");

        // Aktif + show_interim_results=false ise: admin haric 403
        if (result.Status == "aktif")
        {
            var role = await _currentUser.GetRoleAsync(request.OrgId, ct);
            if (role != MemberRole.Admin)
            {
                // ShowInterimResults kontrolu icin detay al
                var detail = await _repo.GetDetailAsync(request.PollId, ct);
                if (detail is not null && !detail.ShowInterimResults)
                    throw AppException.Forbidden("Ara sonuclar oylama tamamlanana kadar gorulemez.");
            }
        }

        return result;
    }
}
