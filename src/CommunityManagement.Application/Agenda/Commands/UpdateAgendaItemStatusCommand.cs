using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.Agenda.Commands;

public record UpdateAgendaItemStatusCommand(
    Guid OrgId, Guid Id, string Status, string? CloseReason
) : IRequest;

public class UpdateAgendaItemStatusCommandHandler : IRequestHandler<UpdateAgendaItemStatusCommand>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IAgendaRepository _repo;

    private static readonly Dictionary<string, HashSet<string>> ValidTransitions = new()
    {
        ["acik"]               = new() { "degerlendiriliyor", "kapali" },
        ["degerlendiriliyor"]  = new() { "acik", "oylamada", "kararlasti", "kapali" },
        ["oylamada"]           = new() { "kararlasti", "kapali" },
    };

    public UpdateAgendaItemStatusCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IAgendaRepository repo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
    }

    public async Task Handle(UpdateAgendaItemStatusCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw AppException.NotFound("Gundem maddesi bulunamadi.");
        if (entity.OrganizationId != request.OrgId)
            throw AppException.NotFound("Gundem maddesi bulunamadi.");

        if (!ValidTransitions.TryGetValue(entity.Status, out var allowed) || !allowed.Contains(request.Status))
            throw AppException.UnprocessableEntity($"'{entity.Status}' durumundan '{request.Status}' durumuna gecis yapilamaz.");

        // oylamada -> kararlasti: iliskili poll aktif olmamali
        if (entity.Status == "oylamada" && request.Status == "kararlasti")
        {
            using var checkConn = _factory.CreateUserConnection();
            var activePollCount = await checkConn.QuerySingleAsync<long>(
                "SELECT COUNT(*) FROM public.polls WHERE agenda_item_id = @AiId AND status = 'aktif'",
                new { AiId = request.Id });
            if (activePollCount > 0)
                throw AppException.UnprocessableEntity("Aktif oylamasi devam eden gundem maddesi kararlasti yapılamaz.");
        }

        var currentUserId = _currentUser.UserId;
        var now = DateTimeOffset.UtcNow;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                """
                UPDATE public.agenda_items
                SET status = @Status, close_reason = @CloseReason, updated_at = @Now
                WHERE id = @Id
                """,
                new
                {
                    request.Id, request.Status,
                    CloseReason = request.Status == "kapali" ? request.CloseReason : null,
                    Now = now.UtcDateTime
                }, tx);

            // kararlasti -> otomatik Decision olustur
            if (request.Status == "kararlasti")
            {
                var decisionId = Guid.NewGuid();
                await conn.ExecuteAsync(
                    """
                    INSERT INTO public.decisions
                        (id, organization_id, agenda_item_id, decided_by, title, status, decided_at, created_at, updated_at)
                    VALUES (@Id, @OrgId, @AiId, @DecidedBy, @Title, 'karar_alindi', @Now, @Now, @Now)
                    """,
                    new
                    {
                        Id = decisionId, OrgId = request.OrgId, AiId = request.Id,
                        DecidedBy = currentUserId, Title = entity.Title, Now = now.UtcDateTime
                    }, tx);
            }

            // Olusturana bildirim
            if (entity.CreatedBy != currentUserId)
            {
                await conn.ExecuteAsync(
                    """
                    INSERT INTO public.notifications
                        (id, organization_id, user_id, type, title, body, reference_type, reference_id, created_at)
                    VALUES (@NId, @OrgId, @UserId, 'agenda_status_changed', @Title, @Body,
                            'agenda_item', @RefId, @Now)
                    """,
                    new
                    {
                        NId = Guid.NewGuid(), OrgId = request.OrgId, UserId = entity.CreatedBy,
                        Title = $"Gundem Durumu: {request.Status}",
                        Body = $"{entity.Title} — {request.Status}",
                        RefId = request.Id, Now = now.UtcDateTime
                    }, tx);
            }

            // Audit log
            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (organization_id, table_name, record_id, actor_id, action, new_values, old_values)
                VALUES (@OrgId, 'agenda_items', @RecordId, @ActorId, 'update', @NewValues::jsonb, @OldValues::jsonb)
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
