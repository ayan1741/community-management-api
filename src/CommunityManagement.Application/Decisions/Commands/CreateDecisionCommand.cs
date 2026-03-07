using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.Decisions.Commands;

public record CreateDecisionCommand(
    Guid OrgId, string Title, string? Description,
    Guid? AgendaItemId, Guid? PollId
) : IRequest<CreateDecisionResult>;

public record CreateDecisionResult(Guid Id);

public class CreateDecisionCommandHandler : IRequestHandler<CreateDecisionCommand, CreateDecisionResult>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;

    public CreateDecisionCommandHandler(ICurrentUserService currentUser, IDbConnectionFactory factory)
    {
        _currentUser = currentUser;
        _factory = factory;
    }

    public async Task<CreateDecisionResult> Handle(CreateDecisionCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        if (string.IsNullOrWhiteSpace(request.Title))
            throw AppException.UnprocessableEntity("Baslik zorunludur.");

        var currentUserId = _currentUser.UserId;
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO public.decisions
                    (id, organization_id, agenda_item_id, poll_id, decided_by, title, description,
                     status, decided_at, created_at, updated_at)
                VALUES
                    (@Id, @OrgId, @AgendaItemId, @PollId, @DecidedBy, @Title, @Description,
                     'karar_alindi', @Now, @Now, @Now)
                """,
                new
                {
                    Id = id, OrgId = request.OrgId, request.AgendaItemId, request.PollId,
                    DecidedBy = currentUserId, Title = request.Title.Trim(),
                    Description = request.Description?.Trim(), Now = now.UtcDateTime
                }, tx);

            // Tum uyelere bildirim (batch)
            await conn.ExecuteAsync(
                """
                INSERT INTO public.notifications
                    (organization_id, user_id, type, title, body, reference_type, reference_id, created_at)
                SELECT @OrgId, om.user_id, 'decision_updated', @Title, @Body, 'decision', @RefId, @Now
                FROM public.organization_members om
                WHERE om.organization_id = @OrgId AND om.status = 'active'
                """,
                new
                {
                    request.OrgId,
                    Title = $"Yeni Karar: {request.Title.Trim()}",
                    Body = "Karar alindi.",
                    RefId = id, Now = now.UtcDateTime
                }, tx);

            // Audit log
            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (organization_id, table_name, record_id, actor_id, action, new_values)
                VALUES (@OrgId, 'decisions', @RecordId, @ActorId, 'insert', @NewValues::jsonb)
                """,
                new
                {
                    OrgId = request.OrgId, RecordId = id, ActorId = currentUserId,
                    NewValues = JsonSerializer.Serialize(new { Title = request.Title.Trim(), Status = "karar_alindi" })
                }, tx);

            await tx.CommitAsync(ct);
            return new CreateDecisionResult(id);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
