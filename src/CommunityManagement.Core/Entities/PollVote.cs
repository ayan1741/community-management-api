namespace CommunityManagement.Core.Entities;

public class PollVote
{
    public Guid Id { get; set; }
    public Guid PollId { get; set; }
    public Guid UserId { get; set; }
    public Guid PollOptionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
