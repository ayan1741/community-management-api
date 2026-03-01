using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Dues.Commands;

public record UpdateDueSettingsCommand(
    Guid OrgId,
    decimal LateFeeRate,
    int LateFeeGraceDays,
    int ReminderDaysBefore
) : IRequest<OrganizationDueSettings>;

public class UpdateDueSettingsCommandHandler : IRequestHandler<UpdateDueSettingsCommand, OrganizationDueSettings>
{
    private readonly IOrganizationDueSettingsRepository _settings;
    private readonly ICurrentUserService _currentUser;

    public UpdateDueSettingsCommandHandler(IOrganizationDueSettingsRepository settings, ICurrentUserService currentUser)
    {
        _settings = settings;
        _currentUser = currentUser;
    }

    public async Task<OrganizationDueSettings> Handle(UpdateDueSettingsCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var settings = new OrganizationDueSettings
        {
            OrganizationId = request.OrgId,
            LateFeeRate = request.LateFeeRate,
            LateFeeGraceDays = request.LateFeeGraceDays,
            ReminderDaysBefore = request.ReminderDaysBefore
        };

        await _settings.UpsertAsync(settings, ct);
        return settings;
    }
}
