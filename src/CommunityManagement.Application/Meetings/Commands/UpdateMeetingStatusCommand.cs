using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.Meetings.Commands;

public record UpdateMeetingStatusCommand(Guid OrgId, Guid Id, string Status) : IRequest;

public class UpdateMeetingStatusCommandHandler : IRequestHandler<UpdateMeetingStatusCommand>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IMeetingRepository _repo;

    public UpdateMeetingStatusCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IMeetingRepository repo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
    }

    public async Task Handle(UpdateMeetingStatusCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        if (request.Status is not ("tamamlandi" or "iptal"))
            throw AppException.UnprocessableEntity("Gecersiz durum. Sadece 'tamamlandi' veya 'iptal' kabul edilir.");

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw AppException.NotFound("Toplanti bulunamadi.");
        if (entity.OrganizationId != request.OrgId)
            throw AppException.NotFound("Toplanti bulunamadi.");
        if (entity.Status != "planlanmis")
            throw AppException.UnprocessableEntity("Sadece planlanmis toplantilar guncellenebilir.");

        var currentUserId = _currentUser.UserId;
        var now = DateTimeOffset.UtcNow;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                "UPDATE public.meetings SET status = @Status, updated_at = @Now WHERE id = @Id",
                new { request.Id, request.Status, Now = now.UtcDateTime }, tx);

            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (organization_id, table_name, record_id, actor_id, action, new_values, old_values)
                VALUES (@OrgId, 'meetings', @RecordId, @ActorId, 'update', @NewValues::jsonb, @OldValues::jsonb)
                """,
                new
                {
                    OrgId = request.OrgId, RecordId = request.Id, ActorId = currentUserId,
                    NewValues = JsonSerializer.Serialize(new { Status = request.Status }),
                    OldValues = JsonSerializer.Serialize(new { Status = entity.Status })
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
