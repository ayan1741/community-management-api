using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Dues.Queries;

public record GetMyPaymentHistoryQuery(Guid OrgId, int Page, int PageSize) : IRequest<PaymentHistoryResult>;

public record PaymentHistoryResult(IReadOnlyList<PaymentHistoryItem> Items, int TotalCount);

public class GetMyPaymentHistoryQueryHandler : IRequestHandler<GetMyPaymentHistoryQuery, PaymentHistoryResult>
{
    private readonly IPaymentRepository _payments;
    private readonly ICurrentUserService _currentUser;

    public GetMyPaymentHistoryQueryHandler(IPaymentRepository payments, ICurrentUserService currentUser)
    {
        _payments = payments;
        _currentUser = currentUser;
    }

    public async Task<PaymentHistoryResult> Handle(GetMyPaymentHistoryQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);
        var (items, totalCount) = await _payments.GetByResidentAsync(
            request.OrgId, _currentUser.UserId, request.Page, request.PageSize, ct);
        return new PaymentHistoryResult(items, totalCount);
    }
}
