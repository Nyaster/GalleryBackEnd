namespace Entities.Models;

public enum RankingPeriod
{
    Daily,
    Weekly,
    Monthly
}

public sealed class RankingSnapshot
{
    public int Id { get; set; }
    public RankingPeriod Period { get; set; }
    public DateTimeOffset PeriodStartUtc { get; set; }
    public DateTimeOffset PeriodEndUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public List<RankingSnapshotEntry> Entries { get; set; } = [];
}

public sealed class RankingSnapshotEntry
{
    public int SnapshotId { get; set; }
    public RankingSnapshot? Snapshot { get; set; }
    public int ImageId { get; set; }
    public int Rank { get; set; }
    public int LikeDelta { get; set; }
}
