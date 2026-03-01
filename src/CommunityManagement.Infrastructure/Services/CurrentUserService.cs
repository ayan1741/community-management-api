using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Services;
using CommunityManagement.Core.Common;
using Dapper;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace CommunityManagement.Infrastructure.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDbConnectionFactory _factory;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor, IDbConnectionFactory factory)
    {
        _httpContextAccessor = httpContextAccessor;
        _factory = factory;
    }

    public Guid UserId
    {
        get
        {
            var sub = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? _httpContextAccessor.HttpContext?.User.FindFirstValue("sub");

            if (sub is null || !Guid.TryParse(sub, out var userId))
                throw AppException.Unauthorized("Kimlik doğrulama gerekli.");

            return userId;
        }
    }

    public async Task RequireRoleAsync(Guid orgId, MemberRole minimumRole, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT role FROM public.organization_members
            WHERE organization_id = @OrgId AND user_id = @UserId AND status = 'active'
            """;
        var role = await conn.QuerySingleOrDefaultAsync<string?>(sql, new { OrgId = orgId, UserId = UserId });

        if (role is null)
            throw AppException.Forbidden("Bu organizasyona erişim izniniz yok.");

        var memberRole = ParseRole(role);
        if (!HasSufficientRole(memberRole, minimumRole))
            throw AppException.Forbidden("Bu işlem için yetkiniz yok.");
    }

    public async Task<MemberStatus> GetMembershipStatusAsync(Guid orgId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT status FROM public.organization_members
            WHERE organization_id = @OrgId AND user_id = @UserId
            """;
        var status = await conn.QuerySingleOrDefaultAsync<string?>(sql, new { OrgId = orgId, UserId = UserId });

        return status switch
        {
            "active" => MemberStatus.Active,
            "suspended" => MemberStatus.Suspended,
            "removed" => MemberStatus.Removed,
            _ => MemberStatus.Removed
        };
    }

    private static MemberRole ParseRole(string role) => role switch
    {
        "admin" => MemberRole.Admin,
        "board_member" => MemberRole.BoardMember,
        "resident" => MemberRole.Resident,
        _ => MemberRole.Resident
    };

    private static bool HasSufficientRole(MemberRole actual, MemberRole minimum) => minimum switch
    {
        MemberRole.Admin => actual == MemberRole.Admin,
        MemberRole.BoardMember => actual is MemberRole.Admin or MemberRole.BoardMember,
        MemberRole.Resident => true,
        _ => false
    };
}
