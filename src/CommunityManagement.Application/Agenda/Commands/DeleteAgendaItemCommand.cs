using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.Agenda.Commands;

public record DeleteAgendaItemCommand(Guid OrgId, Guid Id) : IRequest;

public class DeleteAgendaItemCommandHandler : IRequestHandler<DeleteAgendaItemCommand>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IAgendaRepository _repo;

    public DeleteAgendaItemCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IAgendaRepository repo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
    }

    public async Task Handle(DeleteAgendaItemCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw AppException.NotFound("Gundem maddesi bulunamadi.");
        if (entity.OrganizationId != request.OrgId)
            throw AppException.NotFound("Gundem maddesi bulunamadi.");

        var currentUserId = _currentUser.UserId;
        var role = await _currentUser.GetRoleAsync(request.OrgId, ct);

        // Sakin: kendi + acik
        if (role == MemberRole.Resident)
        {
            if (entity.CreatedBy != currentUserId)
                throw AppException.Forbidden("Sadece kendi gundem maddenizi silebilirsiniz.");
            if (entity.Status != "acik")
                throw AppException.UnprocessableEntity("Sadece acik durumdaki gundem maddeleri silinebilir.");
        }

        // Oylamada veya aktif poll varsa silme engelle
        if (entity.Status == "oylamada")
            throw AppException.UnprocessableEntity("Oylamada olan gundem maddesi silinemez.");

        using var checkConn = _factory.CreateUserConnection();
        var activePollCount = await checkConn.QuerySingleAsync<long>(
            """
            SELECT COUNT(*) FROM public.polls
            WHERE agenda_item_id = @AiId AND status = 'aktif'
            """,
            new { AiId = request.Id });
        if (activePollCount > 0)
            throw AppException.UnprocessableEntity("Aktif oylamasi olan gundem maddesi silinemez.");

        var now = DateTimeOffset.UtcNow;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                """
                UPDATE public.agenda_items
                SET deleted_at = @Now, deleted_by = @UserId, updated_at = @Now
                WHERE id = @Id
                """,
                new { request.Id, Now = now.UtcDateTime, UserId = currentUserId }, tx);

            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (organization_id, table_name, record_id, actor_id, action, old_values)
                VALUES (@OrgId, 'agenda_items', @RecordId, @ActorId, 'delete', @OldValues::jsonb)
                """,
                new
                {
                    OrgId = request.OrgId, RecordId = request.Id, ActorId = currentUserId,
                    OldValues = JsonSerializer.Serialize(new { entity.Title, entity.Status })
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
