using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Auth.Queries;

public record GetMyContextQuery : IRequest<MyContextResult>;

public class GetMyContextQueryHandler : IRequestHandler<GetMyContextQuery, MyContextResult>
{
    private readonly IProfileRepository _profiles;
    private readonly ICurrentUserService _currentUser;

    public GetMyContextQueryHandler(IProfileRepository profiles, ICurrentUserService currentUser)
    {
        _profiles = profiles;
        _currentUser = currentUser;
    }

    public async Task<MyContextResult> Handle(GetMyContextQuery request, CancellationToken ct)
    {
        var result = await _profiles.GetFullContextAsync(_currentUser.UserId, ct);
        return result;
    }
}
