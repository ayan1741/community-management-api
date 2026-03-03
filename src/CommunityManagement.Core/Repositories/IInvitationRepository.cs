using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;

namespace CommunityManagement.Core.Repositories;

public interface IInvitationRepository
{
    Task<Invitation> CreateAsync(Invitation invitation, CancellationToken ct = default);
    Task<Invitation?> GetByIdAsync(Guid invitationId, CancellationToken ct = default);
    Task<Invitation?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<(IReadOnlyList<InvitationListItem> Items, int TotalCount)> GetByOrgIdAsync(
        Guid orgId, CodeStatus? status, Guid? unitId, int page, int pageSize, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid invitationId, CodeStatus status, CancellationToken ct = default);
    Task<bool> HasActiveInvitationForUnitAsync(Guid unitId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetUnitsWithActiveInvitationAsync(IReadOnlyList<Guid> unitIds, CancellationToken ct = default);
    Task<IReadOnlyList<Invitation>> CreateBulkAsync(IReadOnlyList<Invitation> invitations, CancellationToken ct = default);
    Task RevokeBulkByUnitIdsAsync(IReadOnlyList<Guid> unitIds, CancellationToken ct = default);
    Task<InvitationDetail?> GetByCodeWithDetailsAsync(string code, CancellationToken ct = default);
}

public record InvitationDetail(
    Guid Id,
    Guid OrganizationId,
    Guid UnitId,
    string InvitationCode,
    string CodeStatus,
    Guid CreatedBy,
    DateTimeOffset ExpiresAt,
    string OrganizationName,
    string? BlockName,
    string UnitNumber
);

public record InvitationListItem(
    Guid InvitationId,
    string InvitationCode,
    string UnitNumber,
    string BlockName,
    string CodeStatus,
    string? ApplicationStatus,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt
);
