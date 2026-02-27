using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Auth.Commands;

public record RevokeAllSessionsCommand : IRequest;

public class RevokeAllSessionsCommandHandler : IRequestHandler<RevokeAllSessionsCommand>
{
    private readonly ISessionService _sessionService;
    private readonly ICurrentUserService _currentUser;

    public RevokeAllSessionsCommandHandler(ISessionService sessionService, ICurrentUserService currentUser)
    {
        _sessionService = sessionService;
        _currentUser = currentUser;
    }

    public async Task Handle(RevokeAllSessionsCommand request, CancellationToken ct)
    {
        await _sessionService.RevokeAllAsync(_currentUser.UserId, ct);
    }
}
