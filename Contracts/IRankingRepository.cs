using Entities.Models;

namespace Contracts;

public sealed record RankingSnapshotKey(RankingPeriod Period, DateTimeOffset PeriodStartUtc);
public sealed record RankingSnapshotMetadata(int Id, DateTimeOffset PeriodStartUtc, DateTimeOffset PeriodEndUtc);
public sealed record RankedImageCard(int Rank, int LikeDelta, ImageCard Image);
public sealed record RankingPage(int Total, IReadOnlyList<RankedImageCard> Entries);

public interface IRankingRepository
{
    Task<DateTimeOffset?> GetEarliestLikeActivityUtcAsync(CancellationToken cancellationToken = default);
    Task<List<RankingSnapshotKey>> GetSnapshotKeysAsync(CancellationToken cancellationToken = default);
    Task<RankingSnapshotMetadata?> GetSnapshotAsync(RankingPeriod period, DateTimeOffset startUtc, CancellationToken cancellationToken = default);
    Task<RankingPage> GetLivePageAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, int page, int pageSize,
        ImageViewer viewer, IReadOnlyList<AiUsageClassification>? aiUsage, CancellationToken cancellationToken = default);
    Task<RankingPage> GetArchivePageAsync(int snapshotId, int page, int pageSize, ImageViewer viewer,
        IReadOnlyList<AiUsageClassification>? aiUsage, CancellationToken cancellationToken = default);
    Task<bool> TryCreateSnapshotAsync(RankingPeriod period, DateTimeOffset startUtc, DateTimeOffset endUtc,
        DateTimeOffset now, CancellationToken cancellationToken = default);
}
