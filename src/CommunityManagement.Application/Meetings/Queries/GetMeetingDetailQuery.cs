using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Meetings.Queries;

public record GetMeetingDetailQuery(Guid OrgId, Guid Id) : IRequest<GetMeetingDetailResult>;

public record GetMeetingDetailResult(
    MeetingDetailDto Meeting,
    IReadOnlyList<AgendaItemListDto> AgendaItems);

public class GetMeetingDetailQueryHandler : IRequestHandler<GetMeetingDetailQuery, GetMeetingDetailResult>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IMeetingRepository _meetingRepo;
    private readonly IAgendaRepository _agendaRepo;

    public GetMeetingDetailQueryHandler(
        ICurrentUserService currentUser, IMeetingRepository meetingRepo, IAgendaRepository agendaRepo)
    {
        _currentUser = currentUser;
        _meetingRepo = meetingRepo;
        _agendaRepo = agendaRepo;
    }

    public async Task<GetMeetingDetailResult> Handle(GetMeetingDetailQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var detail = await _meetingRepo.GetDetailAsync(request.Id, ct)
            ?? throw AppException.NotFound("Toplanti bulunamadi.");
        if (detail.OrganizationId != request.OrgId)
            throw AppException.NotFound("Toplanti bulunamadi.");

        // Bagli gundem maddeleri
        var (agendaItems, _) = await _agendaRepo.GetListAsync(
            request.OrgId, _currentUser.UserId,
            null, null, request.Id,
            "date", "desc",
            1, 100, ct);

        return new GetMeetingDetailResult(detail, agendaItems);
    }
}
