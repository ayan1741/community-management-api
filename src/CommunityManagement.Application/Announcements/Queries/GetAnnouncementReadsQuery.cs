using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Announcements.Queries;

public record GetAnnouncementReadsQuery(
    Guid OrgId, Guid AnnouncementId,
    string Tab, int Page, int PageSize
) : IRequest<GetAnnouncementReadsResult>;

public record GetAnnouncementReadsResult(
    int TargetMemberCount, int ReadCount, double ReadPercentage,
    IReadOnlyList<AnnouncementReadItem>? Readers, int ReadersTotal,
    IReadOnlyList<AnnouncementUnreadItem>? NonReaders, int NonReadersTotal);

public class GetAnnouncementReadsQueryHandler : IRequestHandler<GetAnnouncementReadsQuery, GetAnnouncementReadsResult>
{
    private readonly IAnnouncementRepository _announcements;
    private readonly ICurrentUserService _currentUser;

    public GetAnnouncementReadsQueryHandler(IAnnouncementRepository announcements, ICurrentUserService currentUser)
    {
        _announcements = announcements;
        _currentUser = currentUser;
    }

    public async Task<GetAnnouncementReadsResult> Handle(GetAnnouncementReadsQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.BoardMember, ct);

        var existing = await _announcements.GetByIdAsync(request.AnnouncementId, ct)
            ?? throw AppException.NotFound("Duyuru bulunamadı.");

        if (existing.OrganizationId != request.OrgId)
            throw AppException.NotFound("Duyuru bulunamadı.");

        var readCount = await _announcements.GetReadCountAsync(request.AnnouncementId, ct);
        var targetMemberCount = existing.TargetMemberCount ?? 0;
        var readPercentage = targetMemberCount > 0 ? (double)readCount / targetMemberCount * 100 : 0;

        // Tab bazlı sayfalama: "readers" veya "non-readers"
        IReadOnlyList<AnnouncementReadItem>? readers = null;
        int readersTotal = 0;
        IReadOnlyList<AnnouncementUnreadItem>? nonReaders = null;
        int nonReadersTotal = 0;

        if (request.Tab == "readers")
            (readers, readersTotal) = await _announcements.GetReadersAsync(request.AnnouncementId, request.Page, request.PageSize, ct);
        else
            (nonReaders, nonReadersTotal) = await _announcements.GetNonReadersAsync(request.AnnouncementId, request.Page, request.PageSize, ct);

        return new GetAnnouncementReadsResult(
            targetMemberCount, readCount, Math.Round(readPercentage, 1),
            readers, readersTotal, nonReaders, nonReadersTotal);
    }
}
