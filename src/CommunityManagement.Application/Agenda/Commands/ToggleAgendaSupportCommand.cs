using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;

namespace CommunityManagement.Application.Agenda.Commands;

public record ToggleAgendaSupportCommand(Guid OrgId, Guid Id) : IRequest<ToggleAgendaSupportResult>;

public record ToggleAgendaSupportResult(bool Supported);

public class ToggleAgendaSupportCommandHandler : IRequestHandler<ToggleAgendaSupportCommand, ToggleAgendaSupportResult>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IAgendaRepository _repo;

    public ToggleAgendaSupportCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IAgendaRepository repo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
    }

    public async Task<ToggleAgendaSupportResult> Handle(ToggleAgendaSupportCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw AppException.NotFound("Gundem maddesi bulunamadi.");
        if (entity.OrganizationId != request.OrgId)
            throw AppException.NotFound("Gundem maddesi bulunamadi.");

        if (entity.Status is not ("acik" or "degerlendiriliyor"))
            throw AppException.UnprocessableEntity("Sadece acik veya degerlendiriliyor durumdaki gundem maddelerine destek verilebilir.");

        var currentUserId = _currentUser.UserId;
        var now = DateTimeOffset.UtcNow;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            // Atomic toggle: try INSERT, if conflict -> DELETE
            var inserted = await conn.ExecuteAsync(
                """
                INSERT INTO public.agenda_supports (agenda_item_id, user_id, created_at)
                VALUES (@AiId, @UserId, @Now)
                ON CONFLICT (agenda_item_id, user_id) DO NOTHING
                """,
                new { AiId = request.Id, UserId = currentUserId, Now = now.UtcDateTime }, tx);

            bool supported;
            if (inserted > 0)
            {
                await conn.ExecuteAsync(
                    "UPDATE public.agenda_items SET support_count = support_count + 1, updated_at = @Now WHERE id = @Id",
                    new { request.Id, Now = now.UtcDateTime }, tx);
                supported = true;
            }
            else
            {
                await conn.ExecuteAsync(
                    "DELETE FROM public.agenda_supports WHERE agenda_item_id = @AiId AND user_id = @UserId",
                    new { AiId = request.Id, UserId = currentUserId }, tx);
                await conn.ExecuteAsync(
                    "UPDATE public.agenda_items SET support_count = GREATEST(support_count - 1, 0), updated_at = @Now WHERE id = @Id",
                    new { request.Id, Now = now.UtcDateTime }, tx);
                supported = false;
            }

            await tx.CommitAsync(ct);
            return new ToggleAgendaSupportResult(supported);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
