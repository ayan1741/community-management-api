using CommunityManagement.Application.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Dues.Commands;

public record CreateDuesPeriodCommand(
    Guid OrgId,
    string Name,
    DateOnly StartDate,
    DateOnly DueDate
) : IRequest<DuesPeriod>;

public class CreateDuesPeriodCommandHandler : IRequestHandler<CreateDuesPeriodCommand, DuesPeriod>
{
    private readonly IDuesPeriodRepository _periods;
    private readonly ICurrentUserService _currentUser;

    public CreateDuesPeriodCommandHandler(IDuesPeriodRepository periods, ICurrentUserService currentUser)
    {
        _periods = periods;
        _currentUser = currentUser;
    }

    public async Task<DuesPeriod> Handle(CreateDuesPeriodCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        if (request.DueDate < request.StartDate)
            throw AppException.UnprocessableEntity("Son ödeme tarihi başlangıç tarihinden önce olamaz.");

        var period = new DuesPeriod
        {
            Id = Guid.NewGuid(),
            OrganizationId = request.OrgId,
            Name = request.Name.Trim(),
            StartDate = request.StartDate,
            DueDate = request.DueDate,
            Status = "draft",
            CreatedBy = _currentUser.UserId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        return await _periods.CreateAsync(period, ct);
    }
}
