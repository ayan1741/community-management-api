using CommunityManagement.Application.Common;
using CommunityManagement.Application.Members.Commands;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using NSubstitute;

namespace CommunityManagement.Tests.Members;

public class ChangeMemberRoleCommandHandlerTests
{
    private readonly IMemberRepository _members = Substitute.For<IMemberRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly ChangeMemberRoleCommandHandler _sut;

    public ChangeMemberRoleCommandHandlerTests()
    {
        _sut = new ChangeMemberRoleCommandHandler(_members, _currentUser);
    }

    [Fact]
    public async Task Handle_ValidRoleChange_UpdatesRole()
    {
        var orgId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        _currentUser.GetMembershipStatusAsync(orgId, Arg.Any<CancellationToken>()).Returns(MemberStatus.Active);
        _members.GetByUserIdAsync(orgId, targetUserId, Arg.Any<CancellationToken>()).Returns(new OrganizationMember
        {
            UserId = targetUserId,
            Role = MemberRole.Resident
        });

        await _sut.Handle(new ChangeMemberRoleCommand(orgId, targetUserId, MemberRole.BoardMember), CancellationToken.None);

        await _members.Received(1).UpdateRoleAsync(orgId, targetUserId, MemberRole.BoardMember, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DemotingLastAdmin_ThrowsAppException422()
    {
        var orgId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        _currentUser.GetMembershipStatusAsync(orgId, Arg.Any<CancellationToken>()).Returns(MemberStatus.Active);
        _members.GetByUserIdAsync(orgId, targetUserId, Arg.Any<CancellationToken>()).Returns(new OrganizationMember
        {
            UserId = targetUserId,
            Role = MemberRole.Admin
        });
        _members.IsLastAdminAsync(orgId, targetUserId, Arg.Any<CancellationToken>()).Returns(true);

        var ex = await Assert.ThrowsAsync<AppException>(
            () => _sut.Handle(new ChangeMemberRoleCommand(orgId, targetUserId, MemberRole.Resident), CancellationToken.None));
        Assert.Equal(422, ex.StatusCode);
    }
}
