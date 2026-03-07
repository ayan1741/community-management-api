using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.Polls.Commands;

public record CreatePollCommand(
    Guid OrgId, string Title, string? Description, string PollType,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    Guid? AgendaItemId, bool ShowInterimResults,
    List<CreatePollOptionDto>? Options
) : IRequest<CreatePollResult>;

public record CreatePollOptionDto(string Label);
public record CreatePollResult(Guid Id);

public class CreatePollCommandHandler : IRequestHandler<CreatePollCommand, CreatePollResult>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IAgendaRepository _agendaRepo;

    public CreatePollCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IAgendaRepository agendaRepo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _agendaRepo = agendaRepo;
    }

    public async Task<CreatePollResult> Handle(CreatePollCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        if (string.IsNullOrWhiteSpace(request.Title))
            throw AppException.UnprocessableEntity("Baslik zorunludur.");
        if (request.PollType is not ("evet_hayir" or "coktan_secmeli"))
            throw AppException.UnprocessableEntity("Gecersiz oylama tipi.");
        if (request.EndsAt <= request.StartsAt)
            throw AppException.UnprocessableEntity("Bitis tarihi baslangictan sonra olmali.");

        // coktan_secmeli: 2-10 secenek
        if (request.PollType == "coktan_secmeli")
        {
            if (request.Options is null || request.Options.Count < 2)
                throw AppException.UnprocessableEntity("Coktan secmeli oylamada en az 2 secenek gerekli.");
            if (request.Options.Count > 10)
                throw AppException.UnprocessableEntity("En fazla 10 secenek eklenebilir.");
        }

        // Agenda item varsa kontrol
        if (request.AgendaItemId.HasValue)
        {
            var ai = await _agendaRepo.GetByIdAsync(request.AgendaItemId.Value, ct)
                ?? throw AppException.NotFound("Gundem maddesi bulunamadi.");
            if (ai.OrganizationId != request.OrgId)
                throw AppException.NotFound("Gundem maddesi bulunamadi.");
            if (ai.Status is "kararlasti" or "kapali")
                throw AppException.UnprocessableEntity("Kapali veya kararlasti durumdaki gundem maddesi icin oylama olusturulamaz.");
        }

        var currentUserId = _currentUser.UserId;
        var now = DateTimeOffset.UtcNow;
        var pollId = Guid.NewGuid();

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            // total_member_count snapshot
            var memberCount = await conn.QuerySingleAsync<long>(
                "SELECT COUNT(*) FROM public.organization_members WHERE organization_id = @OrgId AND status = 'active'",
                new { request.OrgId }, tx);

            await conn.ExecuteAsync(
                """
                INSERT INTO public.polls
                    (id, organization_id, agenda_item_id, created_by, title, description, poll_type,
                     starts_at, ends_at, status, show_interim_results, total_member_count, created_at, updated_at)
                VALUES
                    (@Id, @OrgId, @AgendaItemId, @CreatedBy, @Title, @Description, @PollType,
                     @StartsAt, @EndsAt, 'aktif', @ShowInterim, @MemberCount, @Now, @Now)
                """,
                new
                {
                    Id = pollId, request.OrgId, request.AgendaItemId, CreatedBy = currentUserId,
                    Title = request.Title.Trim(), Description = request.Description?.Trim(),
                    request.PollType,
                    StartsAt = request.StartsAt.UtcDateTime, EndsAt = request.EndsAt.UtcDateTime,
                    ShowInterim = request.ShowInterimResults,
                    MemberCount = (int)memberCount,
                    Now = now.UtcDateTime
                }, tx);

            // Secenekleri ekle
            if (request.PollType == "evet_hayir")
            {
                await conn.ExecuteAsync(
                    "INSERT INTO public.poll_options (id, poll_id, label, vote_count, display_order) VALUES (@Id, @PollId, @Label, 0, @Order)",
                    new[] {
                        new { Id = Guid.NewGuid(), PollId = pollId, Label = "Evet", Order = (short)0 },
                        new { Id = Guid.NewGuid(), PollId = pollId, Label = "Hayir", Order = (short)1 }
                    }, tx);
            }
            else
            {
                var optionParams = request.Options!.Select((opt, i) => new
                {
                    Id = Guid.NewGuid(), PollId = pollId,
                    Label = opt.Label.Trim(), Order = (short)i
                }).ToArray();
                await conn.ExecuteAsync(
                    "INSERT INTO public.poll_options (id, poll_id, label, vote_count, display_order) VALUES (@Id, @PollId, @Label, 0, @Order)",
                    optionParams, tx);
            }

            // Agenda item durumunu -> oylamada
            if (request.AgendaItemId.HasValue)
            {
                await conn.ExecuteAsync(
                    "UPDATE public.agenda_items SET status = 'oylamada', updated_at = @Now WHERE id = @Id",
                    new { Id = request.AgendaItemId.Value, Now = now.UtcDateTime }, tx);
            }

            // Tum uyelere bildirim (batch)
            await conn.ExecuteAsync(
                """
                INSERT INTO public.notifications
                    (organization_id, user_id, type, title, body, reference_type, reference_id, created_at)
                SELECT @OrgId, om.user_id, 'new_poll', @Title, @Body, 'poll', @RefId, @Now
                FROM public.organization_members om
                WHERE om.organization_id = @OrgId AND om.status = 'active'
                """,
                new
                {
                    request.OrgId,
                    Title = $"Yeni Oylama: {request.Title.Trim()}",
                    Body = $"Bitis: {request.EndsAt:dd.MM.yyyy HH:mm}",
                    RefId = pollId, Now = now.UtcDateTime
                }, tx);

            // Email job
            await conn.ExecuteAsync(
                """
                INSERT INTO public.background_jobs (job_type, payload, status, created_at)
                VALUES ('poll_created_email', @Payload::jsonb, 'queued', @Now)
                """,
                new
                {
                    Payload = JsonSerializer.Serialize(new
                    {
                        PollId = pollId, OrganizationId = request.OrgId,
                        Title = request.Title.Trim()
                    }),
                    Now = now.UtcDateTime
                }, tx);

            // Audit log
            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (organization_id, table_name, record_id, actor_id, action, new_values)
                VALUES (@OrgId, 'polls', @RecordId, @ActorId, 'insert', @NewValues::jsonb)
                """,
                new
                {
                    OrgId = request.OrgId, RecordId = pollId, ActorId = currentUserId,
                    NewValues = JsonSerializer.Serialize(new { Title = request.Title.Trim(), request.PollType })
                }, tx);

            await tx.CommitAsync(ct);
            return new CreatePollResult(pollId);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
