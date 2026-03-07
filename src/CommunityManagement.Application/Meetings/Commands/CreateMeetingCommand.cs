using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.Meetings.Commands;

public record CreateMeetingCommand(
    Guid OrgId, string Title, string? Description, DateTimeOffset MeetingDate
) : IRequest<CreateMeetingResult>;

public record CreateMeetingResult(Guid Id);

public class CreateMeetingCommandHandler : IRequestHandler<CreateMeetingCommand, CreateMeetingResult>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;

    public CreateMeetingCommandHandler(ICurrentUserService currentUser, IDbConnectionFactory factory)
    {
        _currentUser = currentUser;
        _factory = factory;
    }

    public async Task<CreateMeetingResult> Handle(CreateMeetingCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        if (string.IsNullOrWhiteSpace(request.Title))
            throw AppException.UnprocessableEntity("Baslik zorunludur.");

        var currentUserId = _currentUser.UserId;
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO public.meetings
                    (id, organization_id, title, description, meeting_date, status, created_by, created_at, updated_at)
                VALUES
                    (@Id, @OrgId, @Title, @Description, @MeetingDate, 'planlanmis', @CreatedBy, @Now, @Now)
                """,
                new
                {
                    Id = id, OrgId = request.OrgId,
                    Title = request.Title.Trim(), Description = request.Description?.Trim(),
                    MeetingDate = request.MeetingDate.UtcDateTime,
                    CreatedBy = currentUserId, Now = now.UtcDateTime
                }, tx);

            // Tum uyelere bildirim (batch)
            await conn.ExecuteAsync(
                """
                INSERT INTO public.notifications
                    (organization_id, user_id, type, title, body, reference_type, reference_id, created_at)
                SELECT @OrgId, om.user_id, 'new_meeting', @Title, @Body, 'meeting', @RefId, @Now
                FROM public.organization_members om
                WHERE om.organization_id = @OrgId AND om.status = 'active'
                """,
                new
                {
                    request.OrgId,
                    Title = $"Yeni Toplanti: {request.Title.Trim()}",
                    Body = $"Tarih: {request.MeetingDate:dd.MM.yyyy HH:mm}",
                    RefId = id, Now = now.UtcDateTime
                }, tx);

            // Email job
            await conn.ExecuteAsync(
                """
                INSERT INTO public.background_jobs (job_type, payload, status, created_at)
                VALUES ('meeting_created_email', @Payload::jsonb, 'queued', @Now)
                """,
                new
                {
                    Payload = JsonSerializer.Serialize(new
                    {
                        MeetingId = id, OrganizationId = request.OrgId,
                        Title = request.Title.Trim()
                    }),
                    Now = now.UtcDateTime
                }, tx);

            // Audit log
            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (organization_id, table_name, record_id, actor_id, action, new_values)
                VALUES (@OrgId, 'meetings', @RecordId, @ActorId, 'insert', @NewValues::jsonb)
                """,
                new
                {
                    OrgId = request.OrgId, RecordId = id, ActorId = currentUserId,
                    NewValues = JsonSerializer.Serialize(new { Title = request.Title.Trim(), Status = "planlanmis" })
                }, tx);

            await tx.CommitAsync(ct);
            return new CreateMeetingResult(id);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
