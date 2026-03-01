using CommunityManagement.Application.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Dues.Commands;

public record ApplyLateFeeCommand(
    Guid OrgId,
    Guid UnitDueId,
    decimal Rate,
    string? Note
) : IRequest<LateFee>;

public class ApplyLateFeeCommandHandler : IRequestHandler<ApplyLateFeeCommand, LateFee>
{
    private readonly IUnitDueRepository _unitDues;
    private readonly IDuesPeriodRepository _periods;
    private readonly ILateFeeRepository _lateFees;
    private readonly IPaymentRepository _payments;
    private readonly IOrganizationDueSettingsRepository _settings;
    private readonly ICurrentUserService _currentUser;

    public ApplyLateFeeCommandHandler(
        IUnitDueRepository unitDues,
        IDuesPeriodRepository periods,
        ILateFeeRepository lateFees,
        IPaymentRepository payments,
        IOrganizationDueSettingsRepository settings,
        ICurrentUserService currentUser)
    {
        _unitDues = unitDues;
        _periods = periods;
        _lateFees = lateFees;
        _payments = payments;
        _settings = settings;
        _currentUser = currentUser;
    }

    public async Task<LateFee> Handle(ApplyLateFeeCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var unitDue = await _unitDues.GetByIdAsync(request.UnitDueId, ct)
            ?? throw AppException.NotFound("Tahakkuk bulunamadı.");

        var period = await _periods.GetByIdAsync(unitDue.PeriodId, ct)
            ?? throw AppException.NotFound("Dönem bulunamadı.");

        if (period.OrganizationId != request.OrgId)
            throw AppException.NotFound("Tahakkuk bulunamadı.");

        if (unitDue.Status is not ("pending" or "partial"))
            throw AppException.UnprocessableEntity("Gecikme zammı yalnızca bekleyen veya kısmi ödenmiş tahakkuklara uygulanabilir.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (period.DueDate >= today)
            throw AppException.UnprocessableEntity("Son ödeme tarihi henüz geçmemiş — gecikme zammı uygulanamaz.");

        var daysOverdue = today.DayNumber - period.DueDate.DayNumber;

        // TBK m.120 — basit faiz: kalan tutar × oran × gün/30
        var totalPaid = await _payments.GetTotalPaidAsync(request.UnitDueId, ct);
        var baseAmount = Math.Max(0, unitDue.Amount - totalPaid);
        var feeAmount = Math.Round(baseAmount * request.Rate * (daysOverdue / 30m), 2);

        if (feeAmount <= 0)
            throw AppException.UnprocessableEntity("Hesaplanan gecikme zammı sıfır — oran veya gecikme süresi yetersiz.");

        var lateFee = new LateFee
        {
            Id = Guid.NewGuid(),
            UnitDueId = request.UnitDueId,
            Amount = feeAmount,
            Rate = request.Rate,
            DaysOverdue = daysOverdue,
            AppliedAt = DateTimeOffset.UtcNow,
            AppliedBy = _currentUser.UserId,
            Status = "active",
            Note = request.Note
        };

        return await _lateFees.CreateAsync(lateFee, ct);
    }
}
