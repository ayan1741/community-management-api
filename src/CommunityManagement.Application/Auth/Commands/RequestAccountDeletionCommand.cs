using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Auth.Commands;

public record RequestAccountDeletionCommand : IRequest<DateTimeOffset>;

public class RequestAccountDeletionCommandHandler : IRequestHandler<RequestAccountDeletionCommand, DateTimeOffset>
{
    private readonly IProfileRepository _profiles;
    private readonly IMemberRepository _members;
    private readonly ISessionService _sessionService;
    private readonly ICurrentUserService _currentUser;

    public RequestAccountDeletionCommandHandler(
        IProfileRepository profiles,
        IMemberRepository members,
        ISessionService sessionService,
        ICurrentUserService currentUser)
    {
        _profiles = profiles;
        _members = members;
        _sessionService = sessionService;
        _currentUser = currentUser;
    }

    public async Task<DateTimeOffset> Handle(RequestAccountDeletionCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        var profile = await _profiles.GetByIdAsync(userId, ct)
            ?? throw AppException.NotFound("Kullanıcı profili bulunamadı.");

        var isLastAdmin = await _members.IsLastAdminInAnyOrgAsync(userId, ct);
        if (isLastAdmin)
            throw AppException.UnprocessableEntity(
                "Bir veya daha fazla organizasyonun tek yöneticisisiniz. Hesabınızı silmeden önce başka bir admin atayın.");

        if (profile.DeletionRequestedAt.HasValue)
            throw AppException.UnprocessableEntity("Hesap silme talebi zaten alınmış.");

        var scheduledDeletionAt = DateTimeOffset.UtcNow.AddDays(30);

        // Mark deletion in DB first — cleanup service uses this, not Supabase ban signal
        await _profiles.MarkDeletionRequestedAsync(userId, DateTimeOffset.UtcNow, ct);

        // Ban user for 30 days in Supabase to prevent login
        await _sessionService.BanUserAsync(userId, days: 30, ct);

        return scheduledDeletionAt;
    }
}
