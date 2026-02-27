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
}

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
