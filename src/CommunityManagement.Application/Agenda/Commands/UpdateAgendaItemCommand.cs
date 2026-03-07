using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.Agenda.Commands;

public record UpdateAgendaItemCommand(
    Guid OrgId, Guid Id, string Title, string? Description, string? Category
) : IRequest;

public class UpdateAgendaItemCommandHandler : IRequestHandler<UpdateAgendaItemCommand>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IAgendaRepository _repo;

    public UpdateAgendaItemCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IAgendaRepository repo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
    }

    public async Task Handle(UpdateAgendaItemCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw AppException.NotFound("Gundem maddesi bulunamadi.");
        if (entity.OrganizationId != request.OrgId)
            throw AppException.NotFound("Gundem maddesi bulunamadi.");

        var currentUserId = _currentUser.UserId;
        var role = await _currentUser.GetRoleAsync(request.OrgId, ct);

        // Sakin: sadece kendi + acik
        if (role == MemberRole.Resident)
        {
            if (entity.CreatedBy != currentUserId)
                throw AppException.Forbidden("Sadece kendi gundem maddenizi guncelleyebilirsiniz.");
            if (entity.Status != "acik")
                throw AppException.UnprocessableEntity("Sadece acik durumdaki gundem maddeleri guncellenebilir.");
        }

        if (string.IsNullOrWhiteSpace(request.Title))
            throw AppException.UnprocessableEntity("Baslik zorunludur.");
        if (request.Title.Trim().Length > 200)
            throw AppException.UnprocessableEntity("Baslik en fazla 200 karakter olabilir.");

        var category = request.Category ?? entity.Category;
        if (category is not ("genel" or "bakim_onarim" or "guvenlik" or "sosyal" or "finansal" or "yonetim"))
            throw AppException.UnprocessableEntity("Gecersiz kategori.");

        var now = DateTimeOffset.UtcNow;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                """
                UPDATE public.agenda_items
                SET title = @Title, description = @Description, category = @Category, updated_at = @Now
                WHERE id = @Id
                """,
                new
                {
                    request.Id,
                    Title = request.Title.Trim(),
                    Description = request.Description?.Trim(),
                    Category = category,
                    Now = now.UtcDateTime
                }, tx);

            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (organization_id, table_name, record_id, actor_id, action, new_values, old_values)
                VALUES (@OrgId, 'agenda_items', @RecordId, @ActorId, 'update', @NewValues::jsonb, @OldValues::jsonb)
                """,
                new
                {
                    OrgId = request.OrgId, RecordId = request.Id, ActorId = currentUserId,
                    NewValues = JsonSerializer.Serialize(new { Title = request.Title.Trim(), Category = category }),
                    OldValues = JsonSerializer.Serialize(new { entity.Title, entity.Category })
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
