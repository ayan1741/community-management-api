using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;

namespace CommunityManagement.Application.Polls.Commands;

public record ExtendPollCommand(Guid OrgId, Guid PollId, DateTimeOffset NewEndsAt) : IRequest;

public class ExtendPollCommandHandler : IRequestHandler<ExtendPollCommand>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IPollRepository _repo;

    public ExtendPollCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IPollRepository repo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
    }

    public async Task Handle(ExtendPollCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var poll = await _repo.GetByIdAsync(request.PollId, ct)
            ?? throw AppException.NotFound("Oylama bulunamadi.");
        if (poll.OrganizationId != request.OrgId)
            throw AppException.NotFound("Oylama bulunamadi.");
        if (poll.Status != "aktif")
            throw AppException.UnprocessableEntity("Sadece aktif oylamalarin suresi uzatilabilir.");
        if (request.NewEndsAt <= poll.EndsAt)
            throw AppException.UnprocessableEntity("Yeni bitis tarihi mevcut bitis tarihinden sonra olmali.");

        var now = DateTimeOffset.UtcNow;

        using var conn = _factory.CreateServiceRoleConnection();
        await conn.ExecuteAsync(
            "UPDATE public.polls SET ends_at = @EndsAt, updated_at = @Now WHERE id = @Id",
            new { Id = request.PollId, EndsAt = request.NewEndsAt.UtcDateTime, Now = now.UtcDateTime });
    }
}
