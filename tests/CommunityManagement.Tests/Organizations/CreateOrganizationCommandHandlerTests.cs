using CommunityManagement.Application.Common;
using CommunityManagement.Application.Organizations.Commands;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using NSubstitute;

namespace CommunityManagement.Tests.Organizations;

public class CreateOrganizationCommandHandlerTests
{
    private readonly IOrganizationRepository _organizations = Substitute.For<IOrganizationRepository>();
    private readonly IMemberRepository _members = Substitute.For<IMemberRepository>();
    private readonly IBlockRepository _blocks = Substitute.For<IBlockRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly CreateOrganizationCommandHandler _sut;

    public CreateOrganizationCommandHandlerTests()
    {
        _sut = new CreateOrganizationCommandHandler(_organizations, _members, _blocks, _currentUser);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesOrgAndAdminMember()
    {
        var userId = Guid.NewGuid();
        _currentUser.UserId.Returns(userId);
        _organizations.ExistsForUserAsync(userId, "Test Sitesi", Arg.Any<CancellationToken>()).Returns(false);

        var command = new CreateOrganizationCommand("Test Sitesi", "site", "Kadıköy", "İstanbul", null);
        var result = await _sut.Handle(command, CancellationToken.None);

        Assert.Equal("Test Sitesi", result.Name);
        Assert.Equal("draft", result.Status);
        Assert.Equal("admin", result.Role);
        await _organizations.Received(1).CreateAsync(Arg.Any<Core.Entities.Organization>(), Arg.Any<CancellationToken>());
        await _members.Received(1).UpsertAsync(
            Arg.Is<Core.Entities.OrganizationMember>(m => m.UserId == userId && m.Role == MemberRole.Admin),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateName_ThrowsAppException422()
    {
        var userId = Guid.NewGuid();
        _currentUser.UserId.Returns(userId);
        _organizations.ExistsForUserAsync(userId, "Var Olan Site", Arg.Any<CancellationToken>()).Returns(true);

        var command = new CreateOrganizationCommand("Var Olan Site", "site", null, null, null);

        var ex = await Assert.ThrowsAsync<AppException>(() => _sut.Handle(command, CancellationToken.None));
        Assert.Equal(422, ex.StatusCode);
    }
}
