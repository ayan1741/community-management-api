using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.Meetings.Commands;

public record UpdateMeetingCommand(
    Guid OrgId, Guid Id, string Title, string? Description, DateTimeOffset MeetingDate
) : IRequest;

public class UpdateMeetingCommandHandler : IRequestHandler<UpdateMeetingCommand>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IMeetingRepository _repo;

    public UpdateMeetingCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IMeetingRepository repo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
    }

    public async Task Handle(UpdateMeetingCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        if (string.IsNullOrWhiteSpace(request.Title))
            throw AppException.UnprocessableEntity("Baslik zorunludur.");

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw AppException.NotFound("Toplanti bulunamadi.");
        if (entity.OrganizationId != request.OrgId)
            throw AppException.NotFound("Toplanti bulunamadi.");

        var currentUserId = _currentUser.UserId;
        var now = DateTimeOffset.UtcNow;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                """
                UPDATE public.meetings
                SET title = @Title, description = @Description, meeting_date = @MeetingDate, updated_at = @Now
                WHERE id = @Id
                """,
                new
                {
                    request.Id, Title = request.Title.Trim(),
                    Description = request.Description?.Trim(),
                    MeetingDate = request.MeetingDate.UtcDateTime,
                    Now = now.UtcDateTime
                }, tx);

            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (organization_id, table_name, record_id, actor_id, action, new_values, old_values)
                VALUES (@OrgId, 'meetings', @RecordId, @ActorId, 'update', @NewValues::jsonb, @OldValues::jsonb)
                """,
                new
                {
                    OrgId = request.OrgId, RecordId = request.Id, ActorId = currentUserId,
                    NewValues = JsonSerializer.Serialize(new { Title = request.Title.Trim() }),
                    OldValues = JsonSerializer.Serialize(new { entity.Title })
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
