using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.Polls.Commands;

public record CancelPollCommand(Guid OrgId, Guid PollId) : IRequest;

public class CancelPollCommandHandler : IRequestHandler<CancelPollCommand>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IPollRepository _repo;

    public CancelPollCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IPollRepository repo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
    }

    public async Task Handle(CancelPollCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var poll = await _repo.GetByIdAsync(request.PollId, ct)
            ?? throw AppException.NotFound("Oylama bulunamadi.");
        if (poll.OrganizationId != request.OrgId)
            throw AppException.NotFound("Oylama bulunamadi.");
        if (poll.Status != "aktif")
            throw AppException.UnprocessableEntity("Sadece aktif oylamalar iptal edilebilir.");

        var currentUserId = _currentUser.UserId;
        var now = DateTimeOffset.UtcNow;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                "UPDATE public.polls SET status = 'iptal', updated_at = @Now WHERE id = @Id",
                new { Id = request.PollId, Now = now.UtcDateTime }, tx);

            // Agenda item -> degerlendiriliyor
            if (poll.AgendaItemId.HasValue)
            {
                await conn.ExecuteAsync(
                    "UPDATE public.agenda_items SET status = 'degerlendiriliyor', updated_at = @Now WHERE id = @Id AND status = 'oylamada'",
                    new { Id = poll.AgendaItemId.Value, Now = now.UtcDateTime }, tx);
            }

            // Audit log
            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (organization_id, table_name, record_id, actor_id, action, new_values, old_values)
                VALUES (@OrgId, 'polls', @RecordId, @ActorId, 'update', @NewValues::jsonb, @OldValues::jsonb)
                """,
                new
                {
                    OrgId = request.OrgId, RecordId = request.PollId, ActorId = currentUserId,
                    NewValues = JsonSerializer.Serialize(new { Status = "iptal" }),
                    OldValues = JsonSerializer.Serialize(new { Status = poll.Status })
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
