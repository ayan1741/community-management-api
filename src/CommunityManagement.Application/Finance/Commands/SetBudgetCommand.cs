using CommunityManagement.Application.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Finance.Commands;

public record SetBudgetCommand(
    Guid OrgId, Guid CategoryId, int Year, int Month, decimal Amount
) : IRequest<FinanceBudget>;

public class SetBudgetCommandHandler : IRequestHandler<SetBudgetCommand, FinanceBudget>
{
    private readonly IFinanceBudgetRepository _budgets;
    private readonly IFinanceCategoryRepository _categories;
    private readonly ICurrentUserService _currentUser;

    public SetBudgetCommandHandler(
        IFinanceBudgetRepository budgets,
        IFinanceCategoryRepository categories,
        ICurrentUserService currentUser)
    {
        _budgets = budgets;
        _categories = categories;
        _currentUser = currentUser;
    }

    public async Task<FinanceBudget> Handle(SetBudgetCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        if (request.Month is < 1 or > 12)
            throw AppException.UnprocessableEntity("Ay 1-12 arasında olmalıdır.");

        if (request.Amount < 0)
            throw AppException.UnprocessableEntity("Bütçe tutarı negatif olamaz.");

        var category = await _categories.GetByIdAsync(request.CategoryId, ct)
            ?? throw AppException.NotFound("Kategori bulunamadı.");

        if (category.OrganizationId != request.OrgId)
            throw AppException.NotFound("Kategori bulunamadı.");

        if (category.Type != "expense")
            throw AppException.UnprocessableEntity("Bütçe sadece gider kategorileri için tanımlanabilir.");

        if (!category.IsActive)
            throw AppException.UnprocessableEntity("Pasif kategoriye bütçe tanımlanamaz.");

        var now = DateTimeOffset.UtcNow;
        var budget = new FinanceBudget
        {
            Id = Guid.NewGuid(),
            OrganizationId = request.OrgId,
            CategoryId = request.CategoryId,
            Year = request.Year,
            Month = request.Month,
            Amount = request.Amount,
            CreatedBy = _currentUser.UserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        return await _budgets.UpsertAsync(budget, ct);
    }
}
