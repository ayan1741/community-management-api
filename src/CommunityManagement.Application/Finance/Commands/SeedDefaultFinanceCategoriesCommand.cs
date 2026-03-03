using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Finance.Commands;

public record SeedDefaultFinanceCategoriesCommand(Guid OrgId) : IRequest;

public class SeedDefaultFinanceCategoriesCommandHandler : IRequestHandler<SeedDefaultFinanceCategoriesCommand>
{
    private readonly IFinanceCategoryRepository _categories;
    private readonly ICurrentUserService _currentUser;

    public SeedDefaultFinanceCategoriesCommandHandler(
        IFinanceCategoryRepository categories,
        ICurrentUserService currentUser)
    {
        _categories = categories;
        _currentUser = currentUser;
    }

    public async Task Handle(SeedDefaultFinanceCategoriesCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var hasCats = await _categories.HasCategoriesAsync(request.OrgId, ct);
        if (hasCats) return; // Idempotent — zaten varsa erken dön

        var now = DateTimeOffset.UtcNow;
        var orgId = request.OrgId;
        var order = 0;

        var categories = new List<FinanceCategory>();

        // --- Gelir Kategorileri ---
        categories.Add(MakeCat(orgId, "Devir Bakiyesi", "income", "piggy-bank", isSystem: true, order++, now));
        categories.Add(MakeCat(orgId, "Kira Gelirleri", "income", "building", false, order++, now));
        categories.Add(MakeCat(orgId, "Faiz Gelirleri", "income", "percent", false, order++, now));
        categories.Add(MakeCat(orgId, "Ceza / Gecikme Zammı", "income", "alert-triangle", false, order++, now));
        categories.Add(MakeCat(orgId, "Diğer Gelirler", "income", "circle-dot", false, order++, now));

        // --- Gider Kategorileri ---
        order = 0;
        categories.Add(MakeCat(orgId, "Elektrik", "expense", "zap", false, order++, now));
        categories.Add(MakeCat(orgId, "Su", "expense", "droplet", false, order++, now));
        categories.Add(MakeCat(orgId, "Doğalgaz / Yakıt", "expense", "flame", false, order++, now));
        categories.Add(MakeCat(orgId, "Temizlik", "expense", "sparkles", false, order++, now));
        categories.Add(MakeCat(orgId, "Güvenlik", "expense", "shield", false, order++, now));
        categories.Add(MakeCat(orgId, "Asansör", "expense", "arrow-up-down", false, order++, now));
        categories.Add(MakeCat(orgId, "Bahçe & Peyzaj", "expense", "trees", false, order++, now));
        categories.Add(MakeCat(orgId, "Havuz", "expense", "waves", false, order++, now));
        categories.Add(MakeCat(orgId, "Sigorta", "expense", "shield-check", false, order++, now));
        categories.Add(MakeCat(orgId, "Personel", "expense", "users", false, order++, now));
        categories.Add(MakeCat(orgId, "Bakım & Onarım", "expense", "wrench", false, order++, now));
        categories.Add(MakeCat(orgId, "Yönetim Gideri", "expense", "briefcase", false, order++, now));
        categories.Add(MakeCat(orgId, "Vergi & Resmi", "expense", "landmark", false, order++, now));
        categories.Add(MakeCat(orgId, "Diğer Giderler", "expense", "circle-dot", false, order++, now));

        await _categories.CreateBulkAsync(categories, ct);
    }

    private static FinanceCategory MakeCat(Guid orgId, string name, string type, string icon, bool isSystem, int sortOrder, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = orgId,
        Name = name,
        Type = type,
        ParentId = null,
        Icon = icon,
        IsSystem = isSystem,
        IsActive = true,
        SortOrder = sortOrder,
        CreatedAt = now,
        UpdatedAt = now
    };
}
