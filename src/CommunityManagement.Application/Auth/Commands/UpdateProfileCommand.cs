using CommunityManagement.Application.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Auth.Commands;

public record UpdateProfileCommand(string? FullName, string? Phone) : IRequest<UserProfile>;

public class UpdateProfileCommandHandler : IRequestHandler<UpdateProfileCommand, UserProfile>
{
    private readonly IProfileRepository _profiles;
    private readonly ICurrentUserService _currentUser;

    public UpdateProfileCommandHandler(IProfileRepository profiles, ICurrentUserService currentUser)
    {
        _profiles = profiles;
        _currentUser = currentUser;
    }

    public async Task<UserProfile> Handle(UpdateProfileCommand request, CancellationToken ct)
    {
        var profile = await _profiles.GetByIdAsync(_currentUser.UserId, ct)
            ?? throw AppException.NotFound("Kullanıcı profili bulunamadı.");

        if (request.FullName is not null)
            profile.FullName = request.FullName;

        if (request.Phone is not null)
            profile.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone;

        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await _profiles.UpdateAsync(profile, ct);
        return profile;
    }
}
