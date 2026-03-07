namespace CommunityManagement.Core.Entities;

public class PollOption
{
    public Guid Id { get; set; }
    public Guid PollId { get; set; }
    public string Label { get; set; } = default!;
    public int VoteCount { get; set; }
    public short DisplayOrder { get; set; }
}
