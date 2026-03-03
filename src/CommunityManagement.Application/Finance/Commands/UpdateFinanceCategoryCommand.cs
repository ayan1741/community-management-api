using CommunityManagement.Application.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Finance.Commands;

public record UpdateFinanceCategoryCommand(
    Guid OrgId, Guid CategoryId, string Name, string? Icon, int SortOrder
) : IRequest<FinanceCategory>;

public class UpdateFinanceCategoryCommandHandler : IRequestHandler<UpdateFinanceCategoryCommand, FinanceCategory>
{
    private readonly IFinanceCategoryRepository _categories;
    private readonly ICurrentUserService _currentUser;

    public UpdateFinanceCategoryCommandHandler(
        IFinanceCategoryRepository categories,
        ICurrentUserService currentUser)
    {
        _categories = categories;
        _currentUser = currentUser;
    }

    public async Task<FinanceCategory> Handle(UpdateFinanceCategoryCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var category = await _categories.GetByIdAsync(request.CategoryId, ct)
            ?? throw AppException.NotFound("Kategori bulunamadı.");

        if (category.OrganizationId != request.OrgId)
            throw AppException.NotFound("Kategori bulunamadı.");

        var exists = await _categories.ExistsByNameAsync(request.OrgId, category.Type, request.Name, request.CategoryId, ct);
        if (exists)
            throw AppException.Conflict("Bu türde aynı isimde bir kategori zaten mevcut.");

        category.Name = request.Name.Trim();
        category.Icon = request.Icon;
        category.SortOrder = request.SortOrder;
        category.UpdatedAt = DateTimeOffset.UtcNow;

        await _categories.UpdateAsync(category, ct);
        return category;
    }
}
