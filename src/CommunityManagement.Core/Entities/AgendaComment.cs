namespace CommunityManagement.Core.Entities;

public class AgendaComment
{
    public Guid Id { get; set; }
    public Guid AgendaItemId { get; set; }
    public Guid UserId { get; set; }
    public string Content { get; set; } = default!;
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
