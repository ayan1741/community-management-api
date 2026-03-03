using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Finance.Queries;

public record GetFinanceCategoriesQuery(
    Guid OrgId, string? Type, bool? IsActive
) : IRequest<IReadOnlyList<FinanceCategoryTreeItem>>;

public record FinanceCategoryTreeItem(
    Guid Id, string Name, string Type, string? Icon,
    bool IsSystem, bool IsActive, int SortOrder,
    IReadOnlyList<FinanceCategoryTreeItem> Children);

public class GetFinanceCategoriesQueryHandler : IRequestHandler<GetFinanceCategoriesQuery, IReadOnlyList<FinanceCategoryTreeItem>>
{
    private readonly IFinanceCategoryRepository _categories;
    private readonly ICurrentUserService _currentUser;

    public GetFinanceCategoriesQueryHandler(
        IFinanceCategoryRepository categories,
        ICurrentUserService currentUser)
    {
        _categories = categories;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<FinanceCategoryTreeItem>> Handle(GetFinanceCategoriesQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var flat = await _categories.GetByOrgIdAsync(request.OrgId, request.Type, request.IsActive, ct);

        // Ağaç yapısına dönüştür
        var lookup = flat.ToLookup(c => c.ParentId);

        var roots = flat
            .Where(c => c.ParentId is null)
            .Select(c => new FinanceCategoryTreeItem(
                c.Id, c.Name, c.Type, c.Icon, c.IsSystem, c.IsActive, c.SortOrder,
                lookup[c.Id]
                    .Select(child => new FinanceCategoryTreeItem(
                        child.Id, child.Name, child.Type, child.Icon,
                        child.IsSystem, child.IsActive, child.SortOrder,
                        Array.Empty<FinanceCategoryTreeItem>()))
                    .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
                    .ToList()))
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .ToList();

        return roots;
    }
}
