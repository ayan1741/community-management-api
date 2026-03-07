using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;

namespace CommunityManagement.Application.Agenda.Commands;

public record CreateAgendaCommentCommand(
    Guid OrgId, Guid AgendaItemId, string Content
) : IRequest<CreateAgendaCommentResult>;

public record CreateAgendaCommentResult(Guid Id);

public class CreateAgendaCommentCommandHandler : IRequestHandler<CreateAgendaCommentCommand, CreateAgendaCommentResult>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IAgendaRepository _repo;

    public CreateAgendaCommentCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IAgendaRepository repo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
    }

    public async Task<CreateAgendaCommentResult> Handle(CreateAgendaCommentCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var entity = await _repo.GetByIdAsync(request.AgendaItemId, ct)
            ?? throw AppException.NotFound("Gundem maddesi bulunamadi.");
        if (entity.OrganizationId != request.OrgId)
            throw AppException.NotFound("Gundem maddesi bulunamadi.");

        if (string.IsNullOrWhiteSpace(request.Content))
            throw AppException.UnprocessableEntity("Yorum icerigi zorunludur.");
        if (request.Content.Trim().Length > 2000)
            throw AppException.UnprocessableEntity("Yorum en fazla 2000 karakter olabilir.");

        var currentUserId = _currentUser.UserId;

        // Rate limit: saatlik 10
        var hourlyCount = await _repo.CountUserCommentsLastHourAsync(request.AgendaItemId, currentUserId, ct);
        if (hourlyCount >= 10)
            throw new AppException("Saatlik yorum limitine ulastiniz (maks. 10).", 429);

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO public.agenda_comments (id, agenda_item_id, user_id, content, is_deleted, created_at)
                VALUES (@Id, @AiId, @UserId, @Content, false, @Now)
                """,
                new
                {
                    Id = id, AiId = request.AgendaItemId, UserId = currentUserId,
                    Content = request.Content.Trim(), Now = now.UtcDateTime
                }, tx);

            await conn.ExecuteAsync(
                "UPDATE public.agenda_items SET comment_count = comment_count + 1, updated_at = @Now WHERE id = @Id",
                new { Id = request.AgendaItemId, Now = now.UtcDateTime }, tx);

            await tx.CommitAsync(ct);
            return new CreateAgendaCommentResult(id);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
