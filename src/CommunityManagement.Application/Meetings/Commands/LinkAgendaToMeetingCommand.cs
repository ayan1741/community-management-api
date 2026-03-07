using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;

namespace CommunityManagement.Application.Meetings.Commands;

public record LinkAgendaToMeetingCommand(
    Guid OrgId, Guid MeetingId, List<Guid> AgendaItemIds
) : IRequest;

public class LinkAgendaToMeetingCommandHandler : IRequestHandler<LinkAgendaToMeetingCommand>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IMeetingRepository _meetingRepo;

    public LinkAgendaToMeetingCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IMeetingRepository meetingRepo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _meetingRepo = meetingRepo;
    }

    public async Task Handle(LinkAgendaToMeetingCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        if (request.AgendaItemIds.Count == 0)
            throw AppException.UnprocessableEntity("En az bir gundem maddesi secilmeli.");

        var meeting = await _meetingRepo.GetByIdAsync(request.MeetingId, ct)
            ?? throw AppException.NotFound("Toplanti bulunamadi.");
        if (meeting.OrganizationId != request.OrgId)
            throw AppException.NotFound("Toplanti bulunamadi.");

        var now = DateTimeOffset.UtcNow;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                """
                UPDATE public.agenda_items
                SET meeting_id = @MeetingId,
                    status = CASE WHEN status = 'acik' THEN 'degerlendiriliyor' ELSE status END,
                    updated_at = @Now
                WHERE id = ANY(@Ids) AND organization_id = @OrgId AND deleted_at IS NULL
                """,
                new
                {
                    Ids = request.AgendaItemIds.ToArray(),
                    MeetingId = request.MeetingId,
                    OrgId = request.OrgId, Now = now.UtcDateTime
                }, tx);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
