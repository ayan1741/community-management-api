using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Finance.Queries;

public record GetBudgetsQuery(
    Guid OrgId, int Year, int? Month
) : IRequest<IReadOnlyList<BudgetWithCategoryItem>>;

public class GetBudgetsQueryHandler : IRequestHandler<GetBudgetsQuery, IReadOnlyList<BudgetWithCategoryItem>>
{
    private readonly IFinanceBudgetRepository _budgets;
    private readonly ICurrentUserService _currentUser;

    public GetBudgetsQueryHandler(
        IFinanceBudgetRepository budgets,
        ICurrentUserService currentUser)
    {
        _budgets = budgets;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<BudgetWithCategoryItem>> Handle(GetBudgetsQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        return request.Month.HasValue
            ? await _budgets.GetByOrgMonthAsync(request.OrgId, request.Year, request.Month.Value, ct)
            : await _budgets.GetByOrgYearAsync(request.OrgId, request.Year, ct);
    }
}
