using CommunityManagement.Core.Enums;
using ApplicationEntity = CommunityManagement.Core.Entities.Application;

namespace CommunityManagement.Core.Repositories;

public interface IApplicationRepository
{
    Task<ApplicationEntity> CreateAsync(ApplicationEntity application, CancellationToken ct = default);
    Task<ApplicationEntity?> GetByIdAsync(Guid applicationId, CancellationToken ct = default);
    Task<(IReadOnlyList<ApplicationListItem> Items, int TotalCount)> GetByOrgIdAsync(
        Guid orgId, ApplicationStatus? status, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<ApplicationEntity>> GetByApplicantAsync(Guid userId, CancellationToken ct = default);
    Task UpdateStatusAsync(
        Guid applicationId, ApplicationStatus status,
        Guid? reviewedBy, string? rejectionReason,
        DateTimeOffset reviewedAt, CancellationToken ct = default);
    Task<bool> HasPendingApplicationAsync(Guid userId, Guid unitId, CancellationToken ct = default);
}

public record ApplicationListItem(
    Guid ApplicationId,
    string ApplicantName,
    string? ApplicantPhone,
    string UnitNumber,
    string BlockName,
    string ResidentType,
    DateTimeOffset SubmittedAt,
    bool DuplicateWarning
);
