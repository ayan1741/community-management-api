using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Finance.Commands;

public record ToggleCategoryActiveCommand(
    Guid OrgId, Guid CategoryId
) : IRequest;

public class ToggleCategoryActiveCommandHandler : IRequestHandler<ToggleCategoryActiveCommand>
{
    private readonly IFinanceCategoryRepository _categories;
    private readonly ICurrentUserService _currentUser;

    public ToggleCategoryActiveCommandHandler(
        IFinanceCategoryRepository categories,
        ICurrentUserService currentUser)
    {
        _categories = categories;
        _currentUser = currentUser;
    }

    public async Task Handle(ToggleCategoryActiveCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var category = await _categories.GetByIdAsync(request.CategoryId, ct)
            ?? throw AppException.NotFound("Kategori bulunamadı.");

        if (category.OrganizationId != request.OrgId)
            throw AppException.NotFound("Kategori bulunamadı.");

        if (category.IsSystem)
            throw AppException.UnprocessableEntity("Sistem kategorisi değiştirilemez.");

        category.IsActive = !category.IsActive;
        category.UpdatedAt = DateTimeOffset.UtcNow;

        await _categories.UpdateAsync(category, ct);

        // Pasife alınıyorsa alt kategorileri de pasif yap
        if (!category.IsActive)
        {
            var hasChildren = await _categories.HasChildrenAsync(request.CategoryId, ct);
            if (hasChildren)
                await _categories.DeactivateChildrenAsync(request.CategoryId, ct);
        }
    }
}
