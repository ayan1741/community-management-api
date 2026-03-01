using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Common;
using Dapper;

namespace CommunityManagement.Infrastructure.Repositories;

public class OrganizationDueSettingsRepository : IOrganizationDueSettingsRepository
{
    private readonly IDbConnectionFactory _factory;

    public OrganizationDueSettingsRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<OrganizationDueSettings> GetOrDefaultAsync(Guid orgId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT organization_id, late_fee_rate, late_fee_grace_days, reminder_days_before
            FROM public.organization_due_settings
            WHERE organization_id = @OrgId
            """;
        var row = await conn.QuerySingleOrDefaultAsync<OrganizationDueSettings>(sql, new { OrgId = orgId });
        return row ?? new OrganizationDueSettings
        {
            OrganizationId = orgId,
            LateFeeRate = 0,
            LateFeeGraceDays = 0,
            ReminderDaysBefore = 0
        };
    }

    public async Task UpsertAsync(OrganizationDueSettings settings, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        // INSERT ... ON CONFLICT (organization_id) DO UPDATE SET ... — her zaman upsert
        const string sql = """
            INSERT INTO public.organization_due_settings
                (organization_id, late_fee_rate, late_fee_grace_days, reminder_days_before, updated_at)
            VALUES
                (@OrganizationId, @LateFeeRate, @LateFeeGraceDays, @ReminderDaysBefore, now())
            ON CONFLICT (organization_id) DO UPDATE SET
                late_fee_rate        = EXCLUDED.late_fee_rate,
                late_fee_grace_days  = EXCLUDED.late_fee_grace_days,
                reminder_days_before = EXCLUDED.reminder_days_before,
                updated_at           = now()
            """;
        await conn.ExecuteAsync(sql, new
        {
            settings.OrganizationId,
            settings.LateFeeRate,
            settings.LateFeeGraceDays,
            settings.ReminderDaysBefore
        });
    }
}
