using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.Agenda.Commands;

public record CreateAgendaItemCommand(
    Guid OrgId, string Title, string? Description, string? Category
) : IRequest<CreateAgendaItemResult>;

public record CreateAgendaItemResult(Guid Id);

public class CreateAgendaItemCommandHandler : IRequestHandler<CreateAgendaItemCommand, CreateAgendaItemResult>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IAgendaRepository _repo;

    public CreateAgendaItemCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IAgendaRepository repo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
    }

    public async Task<CreateAgendaItemResult> Handle(CreateAgendaItemCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        if (string.IsNullOrWhiteSpace(request.Title))
            throw AppException.UnprocessableEntity("Baslik zorunludur.");
        if (request.Title.Trim().Length > 200)
            throw AppException.UnprocessableEntity("Baslik en fazla 200 karakter olabilir.");

        var category = request.Category ?? "genel";
        if (category is not ("genel" or "bakim_onarim" or "guvenlik" or "sosyal" or "finansal" or "yonetim"))
            throw AppException.UnprocessableEntity("Gecersiz kategori.");

        var currentUserId = _currentUser.UserId;

        // Rate limit: gunluk 5, saatlik 3
        var dailyCount = await _repo.CountUserAgendaItemsTodayAsync(request.OrgId, currentUserId, ct);
        if (dailyCount >= 5)
            throw new AppException("Gunluk gundem maddesi limitine ulastiniz (maks. 5).", 429);

        var hourlyCount = await _repo.CountUserAgendaItemsLastHourAsync(request.OrgId, currentUserId, ct);
        if (hourlyCount >= 3)
            throw new AppException("Saatlik gundem maddesi limitine ulastiniz (maks. 3).", 429);

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO public.agenda_items
                    (id, organization_id, created_by, title, description, category, status,
                     is_pinned, support_count, comment_count, created_at, updated_at)
                VALUES
                    (@Id, @OrgId, @CreatedBy, @Title, @Description, @Category, 'acik',
                     false, 0, 0, @Now, @Now)
                """,
                new
                {
                    Id = id, OrgId = request.OrgId, CreatedBy = currentUserId,
                    Title = request.Title.Trim(),
                    Description = request.Description?.Trim(),
                    Category = category,
                    Now = now.UtcDateTime
                }, tx);

            // Admin'lere bildirim (batch)
            await conn.ExecuteAsync(
                """
                INSERT INTO public.notifications
                    (organization_id, user_id, type, title, body, reference_type, reference_id, created_at)
                SELECT @OrgId, om.user_id, 'new_agenda_item', @Title, @Body,
                       'agenda_item', @RefId, @Now
                FROM public.organization_members om
                WHERE om.organization_id = @OrgId AND om.role = 'admin' AND om.status = 'active'
                """,
                new
                {
                    request.OrgId,
                    Title = $"Yeni Gundem: {request.Title.Trim()}",
                    Body = $"Kategori: {category}",
                    RefId = id, Now = now.UtcDateTime
                }, tx);

            // Audit log
            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (organization_id, table_name, record_id, actor_id, action, new_values)
                VALUES (@OrgId, 'agenda_items', @RecordId, @ActorId, 'insert', @NewValues::jsonb)
                """,
                new
                {
                    OrgId = request.OrgId, RecordId = id, ActorId = currentUserId,
                    NewValues = JsonSerializer.Serialize(new { Title = request.Title.Trim(), Category = category })
                }, tx);

            await tx.CommitAsync(ct);
            return new CreateAgendaItemResult(id);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
