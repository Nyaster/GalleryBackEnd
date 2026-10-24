namespace Entities.Models;

public sealed class ForMeFeed
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public string Mode { get; set; } = "Fallback";
    public int[] AiUsage { get; set; } = [];
    public string[] ExcludedTags { get; set; } = [];
    public int PageSize { get; set; }
    public int Total { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public List<ForMeFeedEntry> Entries { get; set; } = [];
}

public sealed class ForMeFeedEntry
{
    public Guid FeedId { get; set; }
    public int Rank { get; set; }
    public int ImageId { get; set; }
}
