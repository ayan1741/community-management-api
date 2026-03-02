using CommunityManagement.Application.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Invitations.Commands;

public record BulkCreateInvitationCodesCommand(
    Guid OrgId,
    IReadOnlyList<Guid> UnitIds
) : IRequest<BulkInvitationResult>;

public record BulkInvitationResult(
    IReadOnlyList<BulkInvitationResultItem> Items,
    int TotalCreated
);

public record BulkInvitationResultItem(
    Guid InvitationId,
    Guid UnitId,
    string UnitNumber,
    string BlockName,
    string InvitationCode,
    DateTimeOffset ExpiresAt,
    bool HadExistingCode
);

public class BulkCreateInvitationCodesCommandHandler : IRequestHandler<BulkCreateInvitationCodesCommand, BulkInvitationResult>
{
    private readonly IInvitationRepository _invitations;
    private readonly IUnitRepository _units;
    private readonly ICurrentUserService _currentUser;

    public BulkCreateInvitationCodesCommandHandler(
        IInvitationRepository invitations,
        IUnitRepository units,
        ICurrentUserService currentUser)
    {
        _invitations = invitations;
        _units = units;
        _currentUser = currentUser;
    }

    public async Task<BulkInvitationResult> Handle(BulkCreateInvitationCodesCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        if (request.UnitIds.Count == 0)
            throw AppException.UnprocessableEntity("En az bir daire seçilmelidir.");
        if (request.UnitIds.Count > 200)
            throw AppException.UnprocessableEntity("En fazla 200 daire seçilebilir.");

        // Tüm dairelerin bu org'a ait olduğunu doğrula (batch kontrol)
        var orgUnits = await _units.GetDropdownByOrgIdAsync(request.OrgId, ct);
        var orgUnitMap = orgUnits.ToDictionary(u => u.Id);
        foreach (var unitId in request.UnitIds)
        {
            if (!orgUnitMap.ContainsKey(unitId))
                throw AppException.NotFound($"Daire bulunamadı: {unitId}");
        }

        // Aktif kodu olan daireleri tespit et
        var unitsWithActiveCode = await _invitations.GetUnitsWithActiveInvitationAsync(request.UnitIds, ct);
        var hadExistingCodeSet = new HashSet<Guid>(unitsWithActiveCode);

        // Eski aktif kodları revoke et
        if (unitsWithActiveCode.Count > 0)
            await _invitations.RevokeBulkByUnitIdsAsync(unitsWithActiveCode, ct);

        // Yeni davet kodları oluştur
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddDays(7);
        var userId = _currentUser.UserId;

        var invitationsToCreate = request.UnitIds.Select(unitId => new Invitation
        {
            Id = Guid.NewGuid(),
            OrganizationId = request.OrgId,
            UnitId = unitId,
            InvitationCode = GenerateCode(),
            CodeStatus = CodeStatus.Active,
            CreatedBy = userId,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();

        var created = await _invitations.CreateBulkAsync(invitationsToCreate, ct);

        var items = created.Select(inv =>
        {
            var unitInfo = orgUnitMap[inv.UnitId];
            return new BulkInvitationResultItem(
                inv.Id,
                inv.UnitId,
                unitInfo.UnitNumber,
                unitInfo.BlockName,
                inv.InvitationCode,
                inv.ExpiresAt,
                hadExistingCodeSet.Contains(inv.UnitId));
        }).ToList();

        return new BulkInvitationResult(items, items.Count);
    }

    private static string GenerateCode()
    {
        var hex = Guid.NewGuid().ToString("N")[..8].ToUpper();
        return $"{hex[..4]}-{hex[4..]}";
    }
}
