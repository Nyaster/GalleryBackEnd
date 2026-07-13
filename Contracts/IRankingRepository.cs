using Entities.Models;

namespace Contracts;

public sealed record RankingScore(int ImageId, int LikeDelta, DateTimeOffset UploadedAtUtc);

public interface IRankingRepository
{
    Task<DateTimeOffset?> GetEarliestLikeActivityUtcAsync(CancellationToken cancellationToken = default);
    Task<List<RankingScore>> GetLiveScoresAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken = default);
    Task<RankingSnapshot?> GetSnapshotAsync(RankingPeriod period, DateTimeOffset startUtc, bool trackChanges, CancellationToken cancellationToken = default);
    Task AddSnapshotAsync(RankingSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<List<(RankingSnapshotEntry Entry, AppImage Image)>> GetSnapshotEntriesAsync(int snapshotId, CancellationToken cancellationToken = default);
    Task<List<AppImage>> GetDiscoverableImagesAsync(IEnumerable<int> imageIds, CancellationToken cancellationToken = default);
}
