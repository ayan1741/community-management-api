using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Applications.Commands;

public record WithdrawApplicationCommand(Guid ApplicationId) : IRequest;

public class WithdrawApplicationCommandHandler : IRequestHandler<WithdrawApplicationCommand>
{
    private readonly IApplicationRepository _applications;
    private readonly ICurrentUserService _currentUser;

    public WithdrawApplicationCommandHandler(IApplicationRepository applications, ICurrentUserService currentUser)
    {
        _applications = applications;
        _currentUser = currentUser;
    }

    public async Task Handle(WithdrawApplicationCommand request, CancellationToken ct)
    {
        var application = await _applications.GetByIdAsync(request.ApplicationId, ct)
            ?? throw AppException.NotFound("Başvuru bulunamadı.");

        if (application.ApplicantUserId != _currentUser.UserId)
            throw AppException.Forbidden("Bu başvuruya erişim izniniz yok.");

        if (application.ApplicationStatus != ApplicationStatus.Pending)
            throw AppException.UnprocessableEntity("Yalnızca bekleyen başvurular geri çekilebilir.");

        await _applications.UpdateStatusAsync(
            request.ApplicationId, ApplicationStatus.Withdrawn,
            null, null, DateTimeOffset.UtcNow, ct);
    }
}
