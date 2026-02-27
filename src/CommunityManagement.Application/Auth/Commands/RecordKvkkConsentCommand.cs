using CommunityManagement.Application.Common;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Auth.Commands;

public record RecordKvkkConsentCommand : IRequest<DateTimeOffset>;

public class RecordKvkkConsentCommandHandler : IRequestHandler<RecordKvkkConsentCommand, DateTimeOffset>
{
    private readonly IProfileRepository _profiles;
    private readonly ICurrentUserService _currentUser;

    public RecordKvkkConsentCommandHandler(IProfileRepository profiles, ICurrentUserService currentUser)
    {
        _profiles = profiles;
        _currentUser = currentUser;
    }

    public async Task<DateTimeOffset> Handle(RecordKvkkConsentCommand request, CancellationToken ct)
    {
        var profile = await _profiles.GetByIdAsync(_currentUser.UserId, ct)
            ?? throw AppException.NotFound("Kullanıcı profili bulunamadı.");

        if (profile.KvkkConsentAt.HasValue)
            throw AppException.UnprocessableEntity("KVKK onayı zaten verilmiş.");

        var consentAt = DateTimeOffset.UtcNow;
        await _profiles.RecordKvkkConsentAsync(_currentUser.UserId, consentAt, ct);
        return consentAt;
    }
}
