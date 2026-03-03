using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Finance.Queries;

public record GetOpeningBalanceQuery(Guid OrgId) : IRequest<FinanceRecord?>;

public class GetOpeningBalanceQueryHandler : IRequestHandler<GetOpeningBalanceQuery, FinanceRecord?>
{
    private readonly IFinanceRecordRepository _records;
    private readonly ICurrentUserService _currentUser;

    public GetOpeningBalanceQueryHandler(
        IFinanceRecordRepository records,
        ICurrentUserService currentUser)
    {
        _records = records;
        _currentUser = currentUser;
    }

    public async Task<FinanceRecord?> Handle(GetOpeningBalanceQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);
        return await _records.GetOpeningBalanceAsync(request.OrgId, ct);
    }
}
