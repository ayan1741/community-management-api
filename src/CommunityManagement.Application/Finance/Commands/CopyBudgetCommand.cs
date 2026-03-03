using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Finance.Commands;

public record CopyBudgetCommand(
    Guid OrgId, int FromYear, int FromMonth, int ToYear, int ToMonth
) : IRequest<int>;

public class CopyBudgetCommandHandler : IRequestHandler<CopyBudgetCommand, int>
{
    private readonly IFinanceBudgetRepository _budgets;
    private readonly ICurrentUserService _currentUser;

    public CopyBudgetCommandHandler(
        IFinanceBudgetRepository budgets,
        ICurrentUserService currentUser)
    {
        _budgets = budgets;
        _currentUser = currentUser;
    }

    public async Task<int> Handle(CopyBudgetCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        if (request.FromMonth is < 1 or > 12 || request.ToMonth is < 1 or > 12)
            throw AppException.UnprocessableEntity("Ay 1-12 arasında olmalıdır.");

        var count = await _budgets.CopyMonthAsync(
            request.OrgId, request.FromYear, request.FromMonth,
            request.ToYear, request.ToMonth, _currentUser.UserId, ct);

        return count;
    }
}
