namespace CommunityManagement.Core.Entities;

public class AgendaSupport
{
    public Guid AgendaItemId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
