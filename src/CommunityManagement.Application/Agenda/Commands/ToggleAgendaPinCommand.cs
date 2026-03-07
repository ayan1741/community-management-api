using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;

namespace CommunityManagement.Application.Agenda.Commands;

public record ToggleAgendaPinCommand(Guid OrgId, Guid Id) : IRequest;

public class ToggleAgendaPinCommandHandler : IRequestHandler<ToggleAgendaPinCommand>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IAgendaRepository _repo;

    public ToggleAgendaPinCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IAgendaRepository repo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
    }

    public async Task Handle(ToggleAgendaPinCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw AppException.NotFound("Gundem maddesi bulunamadi.");
        if (entity.OrganizationId != request.OrgId)
            throw AppException.NotFound("Gundem maddesi bulunamadi.");

        var now = DateTimeOffset.UtcNow;

        using var conn = _factory.CreateServiceRoleConnection();
        await conn.ExecuteAsync(
            """
            UPDATE public.agenda_items
            SET is_pinned = NOT is_pinned, updated_at = @Now
            WHERE id = @Id
            """,
            new { request.Id, Now = now.UtcDateTime });
    }
}
