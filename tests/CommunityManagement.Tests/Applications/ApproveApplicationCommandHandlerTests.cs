using CommunityManagement.Application.Applications.Commands;
using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using NSubstitute;
using ApplicationEntity = CommunityManagement.Core.Entities.Application;

namespace CommunityManagement.Tests.Applications;

public class ApproveApplicationCommandHandlerTests
{
    private readonly IApplicationRepository _applications = Substitute.For<IApplicationRepository>();
    private readonly IMemberRepository _members = Substitute.For<IMemberRepository>();
    private readonly IUnitResidentRepository _unitResidents = Substitute.For<IUnitResidentRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IDbConnectionFactory _factory = Substitute.For<IDbConnectionFactory>();
    private readonly ApproveApplicationCommandHandler _sut;

    public ApproveApplicationCommandHandlerTests()
    {
        _sut = new ApproveApplicationCommandHandler(_applications, _members, _unitResidents, _currentUser, _factory);
    }

    // Not: Başarılı onay senaryosu artık inline SQL + transaction kullanıyor,
    // Dapper static extension'ları NSubstitute ile mocklanamaz.
    // Bu senaryo smoke/integration test ile doğrulanır.

    [Fact]
    public async Task Handle_AlreadyApprovedApplication_ThrowsAppException422()
    {
        var orgId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        _currentUser.GetMembershipStatusAsync(orgId, Arg.Any<CancellationToken>()).Returns(MemberStatus.Active);
        _applications.GetByIdAsync(appId, Arg.Any<CancellationToken>()).Returns(new ApplicationEntity
        {
            Id = appId,
            OrganizationId = orgId,
            ApplicationStatus = ApplicationStatus.Approved
        });

        var ex = await Assert.ThrowsAsync<AppException>(
            () => _sut.Handle(new ApproveApplicationCommand(orgId, appId), CancellationToken.None));
        Assert.Equal(422, ex.StatusCode);
    }

    [Fact]
    public async Task Handle_WrongOrganization_ThrowsAppException403()
    {
        var orgId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var differentOrgId = Guid.NewGuid();

        _currentUser.GetMembershipStatusAsync(orgId, Arg.Any<CancellationToken>()).Returns(MemberStatus.Active);
        _applications.GetByIdAsync(appId, Arg.Any<CancellationToken>()).Returns(new ApplicationEntity
        {
            Id = appId,
            OrganizationId = differentOrgId,
            ApplicationStatus = ApplicationStatus.Pending
        });

        var ex = await Assert.ThrowsAsync<AppException>(
            () => _sut.Handle(new ApproveApplicationCommand(orgId, appId), CancellationToken.None));
        Assert.Equal(403, ex.StatusCode);
    }
}
