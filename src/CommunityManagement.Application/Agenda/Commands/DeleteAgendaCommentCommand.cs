using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;

namespace CommunityManagement.Application.Agenda.Commands;

public record DeleteAgendaCommentCommand(Guid OrgId, Guid AgendaItemId, Guid CommentId) : IRequest;

public class DeleteAgendaCommentCommandHandler : IRequestHandler<DeleteAgendaCommentCommand>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IAgendaRepository _repo;

    public DeleteAgendaCommentCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IAgendaRepository repo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
    }

    public async Task Handle(DeleteAgendaCommentCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var entity = await _repo.GetByIdAsync(request.AgendaItemId, ct)
            ?? throw AppException.NotFound("Gundem maddesi bulunamadi.");
        if (entity.OrganizationId != request.OrgId)
            throw AppException.NotFound("Gundem maddesi bulunamadi.");

        var comment = await _repo.GetCommentByIdAsync(request.CommentId, ct)
            ?? throw AppException.NotFound("Yorum bulunamadi.");
        if (comment.AgendaItemId != request.AgendaItemId)
            throw AppException.NotFound("Yorum bulunamadi.");
        if (comment.IsDeleted)
            throw AppException.UnprocessableEntity("Yorum zaten silinmis.");

        var currentUserId = _currentUser.UserId;
        var role = await _currentUser.GetRoleAsync(request.OrgId, ct);

        // Sakin: sadece kendi yorumu
        if (role == MemberRole.Resident && comment.UserId != currentUserId)
            throw AppException.Forbidden("Sadece kendi yorumunuzu silebilirsiniz.");

        var now = DateTimeOffset.UtcNow;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                "UPDATE public.agenda_comments SET is_deleted = true WHERE id = @Id",
                new { Id = request.CommentId }, tx);

            await conn.ExecuteAsync(
                "UPDATE public.agenda_items SET comment_count = GREATEST(comment_count - 1, 0), updated_at = @Now WHERE id = @Id",
                new { Id = request.AgendaItemId, Now = now.UtcDateTime }, tx);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
