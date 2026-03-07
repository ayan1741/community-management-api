namespace CommunityManagement.Core.Entities;

public class Poll
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? AgendaItemId { get; set; }
    public Guid CreatedBy { get; set; }
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public string PollType { get; set; } = default!;     // evet_hayir|coktan_secmeli
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string Status { get; set; } = default!;       // aktif|kapandi|iptal
    public bool ShowInterimResults { get; set; }
    public int TotalMemberCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
