using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Organizations.Commands;

public record UpdateOrganizationCommand(
    Guid OrgId,
    string Name,
    string? ContactPhone
) : IRequest;

public class UpdateOrganizationCommandHandler : IRequestHandler<UpdateOrganizationCommand>
{
    private readonly IOrganizationRepository _organizations;
    private readonly IBlockRepository _blocks;
    private readonly ICurrentUserService _currentUser;

    public UpdateOrganizationCommandHandler(
        IOrganizationRepository organizations,
        IBlockRepository blocks,
        ICurrentUserService currentUser)
    {
        _organizations = organizations;
        _blocks = blocks;
        _currentUser = currentUser;
    }

    public async Task Handle(UpdateOrganizationCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        if (string.IsNullOrWhiteSpace(request.Name))
            throw AppException.UnprocessableEntity("Organizasyon adı boş olamaz.");

        var org = await _organizations.GetByIdAsync(request.OrgId, ct)
            ?? throw AppException.NotFound("Organizasyon bulunamadı.");

        var oldName = org.Name;
        org.Name = request.Name;
        org.ContactPhone = request.ContactPhone;

        await _organizations.UpdateAsync(org, ct);

        // Apartman tipinde ad senkronizasyonu: varsayılan blok adını güncelle
        if (org.OrgType == "apartment" && oldName != request.Name)
        {
            var defaultBlock = await _blocks.GetDefaultByOrgIdAsync(request.OrgId, ct);
            if (defaultBlock != null)
            {
                defaultBlock.Name = request.Name;
                defaultBlock.UpdatedAt = DateTimeOffset.UtcNow;
                await _blocks.UpdateAsync(defaultBlock, ct);
            }
        }
    }
}
