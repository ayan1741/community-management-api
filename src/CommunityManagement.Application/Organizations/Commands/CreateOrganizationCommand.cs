using CommunityManagement.Application.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Organizations.Commands;

public record CreateOrganizationCommand(
    string Name,
    string OrgType,
    string? AddressDistrict,
    string? AddressCity,
    string? ContactPhone
) : IRequest<CreateOrganizationResult>;

public record CreateOrganizationResult(
    Guid OrganizationId,
    string Name,
    string Status,
    string Role,
    Guid? DefaultBlockId
);

public class CreateOrganizationCommandHandler : IRequestHandler<CreateOrganizationCommand, CreateOrganizationResult>
{
    private readonly IOrganizationRepository _organizations;
    private readonly IMemberRepository _members;
    private readonly IBlockRepository _blocks;
    private readonly ICurrentUserService _currentUser;

    public CreateOrganizationCommandHandler(
        IOrganizationRepository organizations,
        IMemberRepository members,
        IBlockRepository blocks,
        ICurrentUserService currentUser)
    {
        _organizations = organizations;
        _members = members;
        _blocks = blocks;
        _currentUser = currentUser;
    }

    public async Task<CreateOrganizationResult> Handle(CreateOrganizationCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        var exists = await _organizations.ExistsForUserAsync(userId, request.Name, ct);
        if (exists)
            throw AppException.UnprocessableEntity("Bu isimde bir organizasyon zaten mevcut.");

        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            OrgType = request.OrgType,
            Status = "draft",
            CreatedBy = userId,
            AddressDistrict = request.AddressDistrict,
            AddressCity = request.AddressCity,
            ContactPhone = request.ContactPhone,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _organizations.CreateAsync(org, ct);

        var member = new OrganizationMember
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            UserId = userId,
            Role = MemberRole.Admin,
            Status = MemberStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _members.UpsertAsync(member, ct);

        Guid? defaultBlockId = null;
        if (request.OrgType == "apartment")
        {
            var defaultBlock = new Block
            {
                Id = Guid.NewGuid(),
                OrganizationId = org.Id,
                Name = org.Name,
                BlockType = "residential",
                IsDefault = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var created = await _blocks.CreateAsync(defaultBlock, ct);
            defaultBlockId = created.Id;
        }

        return new CreateOrganizationResult(org.Id, org.Name, org.Status, "admin", defaultBlockId);
    }
}
