using CommunityManagement.Application.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Finance.Commands;

public record CreateFinanceCategoryCommand(
    Guid OrgId, string Name, string Type,
    Guid? ParentId, string? Icon, int SortOrder
) : IRequest<FinanceCategory>;

public class CreateFinanceCategoryCommandHandler : IRequestHandler<CreateFinanceCategoryCommand, FinanceCategory>
{
    private readonly IFinanceCategoryRepository _categories;
    private readonly ICurrentUserService _currentUser;

    public CreateFinanceCategoryCommandHandler(
        IFinanceCategoryRepository categories,
        ICurrentUserService currentUser)
    {
        _categories = categories;
        _currentUser = currentUser;
    }

    public async Task<FinanceCategory> Handle(CreateFinanceCategoryCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        if (request.Type is not ("income" or "expense"))
            throw AppException.UnprocessableEntity("Geçersiz kategori türü. 'income' veya 'expense' olmalıdır.");

        if (request.ParentId.HasValue)
        {
            var parent = await _categories.GetByIdAsync(request.ParentId.Value, ct)
                ?? throw AppException.NotFound("Üst kategori bulunamadı.");

            if (parent.OrganizationId != request.OrgId)
                throw AppException.NotFound("Üst kategori bulunamadı.");

            if (parent.ParentId is not null)
                throw AppException.UnprocessableEntity("Maksimum 2 seviye desteklenir. Üst kategorinin zaten bir ebeveyni var.");

            if (parent.Type != request.Type)
                throw AppException.UnprocessableEntity("Alt kategori, üst kategoriyle aynı türde olmalıdır.");
        }

        var exists = await _categories.ExistsByNameAsync(request.OrgId, request.Type, request.Name, null, ct);
        if (exists)
            throw AppException.Conflict("Bu türde aynı isimde bir kategori zaten mevcut.");

        var now = DateTimeOffset.UtcNow;
        var category = new FinanceCategory
        {
            Id = Guid.NewGuid(),
            OrganizationId = request.OrgId,
            Name = request.Name.Trim(),
            Type = request.Type,
            ParentId = request.ParentId,
            Icon = request.Icon,
            IsSystem = false,
            IsActive = true,
            SortOrder = request.SortOrder,
            CreatedAt = now,
            UpdatedAt = now
        };

        return await _categories.CreateAsync(category, ct);
    }
}
