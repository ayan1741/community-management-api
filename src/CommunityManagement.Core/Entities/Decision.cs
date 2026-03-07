namespace CommunityManagement.Core.Entities;

public class Decision
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? AgendaItemId { get; set; }
    public Guid? PollId { get; set; }
    public Guid DecidedBy { get; set; }
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public string Status { get; set; } = default!;       // karar_alindi|uygulamada|tamamlandi|iptal
    public DateTimeOffset DecidedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
