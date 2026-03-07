using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;

namespace CommunityManagement.Application.Polls.Commands;

public record CastVoteCommand(Guid OrgId, Guid PollId, Guid PollOptionId) : IRequest;

public class CastVoteCommandHandler : IRequestHandler<CastVoteCommand>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IPollRepository _repo;

    public CastVoteCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IPollRepository repo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
    }

    public async Task Handle(CastVoteCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var poll = await _repo.GetByIdAsync(request.PollId, ct)
            ?? throw AppException.NotFound("Oylama bulunamadi.");
        if (poll.OrganizationId != request.OrgId)
            throw AppException.NotFound("Oylama bulunamadi.");
        if (poll.Status != "aktif")
            throw AppException.UnprocessableEntity("Bu oylama aktif degil.");

        var now = DateTimeOffset.UtcNow;
        if (now < poll.StartsAt || now > poll.EndsAt)
            throw AppException.UnprocessableEntity("Oylama suresi disinda oy kullanilamaz.");

        var currentUserId = _currentUser.UserId;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            // Secenegin poll'a ait oldugundan emin ol
            var optionPollId = await conn.QuerySingleOrDefaultAsync<Guid?>(
                "SELECT poll_id FROM public.poll_options WHERE id = @OptionId",
                new { OptionId = request.PollOptionId }, tx);
            if (optionPollId is null || optionPollId.Value != request.PollId)
                throw AppException.UnprocessableEntity("Secilen secenek bu oylamaya ait degil.");

            // Mevcut oy kontrol
            var existingOptionId = await conn.QuerySingleOrDefaultAsync<Guid?>(
                "SELECT poll_option_id FROM public.poll_votes WHERE poll_id = @PollId AND user_id = @UserId",
                new { request.PollId, UserId = currentUserId }, tx);

            if (existingOptionId.HasValue)
            {
                if (existingOptionId.Value == request.PollOptionId)
                {
                    // Ayni secenek — NOP
                    await tx.CommitAsync(ct);
                    return;
                }

                // Farkli secenek — guncelle
                await conn.ExecuteAsync(
                    "UPDATE public.poll_votes SET poll_option_id = @NewOptionId, updated_at = @Now WHERE poll_id = @PollId AND user_id = @UserId",
                    new { NewOptionId = request.PollOptionId, Now = now.UtcDateTime, request.PollId, UserId = currentUserId }, tx);

                // Eski secenek -1
                await conn.ExecuteAsync(
                    "UPDATE public.poll_options SET vote_count = GREATEST(vote_count - 1, 0) WHERE id = @Id",
                    new { Id = existingOptionId.Value }, tx);

                // Yeni secenek +1
                await conn.ExecuteAsync(
                    "UPDATE public.poll_options SET vote_count = vote_count + 1 WHERE id = @Id",
                    new { Id = request.PollOptionId }, tx);
            }
            else
            {
                // Yeni oy
                await conn.ExecuteAsync(
                    """
                    INSERT INTO public.poll_votes (id, poll_id, user_id, poll_option_id, created_at, updated_at)
                    VALUES (@Id, @PollId, @UserId, @OptionId, @Now, @Now)
                    """,
                    new
                    {
                        Id = Guid.NewGuid(), request.PollId, UserId = currentUserId,
                        OptionId = request.PollOptionId, Now = now.UtcDateTime
                    }, tx);

                await conn.ExecuteAsync(
                    "UPDATE public.poll_options SET vote_count = vote_count + 1 WHERE id = @Id",
                    new { Id = request.PollOptionId }, tx);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
