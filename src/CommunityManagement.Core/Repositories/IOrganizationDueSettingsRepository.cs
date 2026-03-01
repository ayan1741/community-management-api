namespace CommunityManagement.Core.Repositories;

public interface IOrganizationDueSettingsRepository
{
    // Kayıt yoksa default (rate=0, grace=0, reminder=0) döner — INSERT yapmaz
    Task<OrganizationDueSettings> GetOrDefaultAsync(Guid orgId, CancellationToken ct = default);
    // INSERT ... ON CONFLICT (organization_id) DO UPDATE SET ... — her zaman upsert
    Task UpsertAsync(OrganizationDueSettings settings, CancellationToken ct = default);
}

public class OrganizationDueSettings
{
    public Guid OrganizationId { get; set; }
    public decimal LateFeeRate { get; set; }
    public int LateFeeGraceDays { get; set; }
    public int ReminderDaysBefore { get; set; }
}
