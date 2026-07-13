using Contracts;
using Entities.Models;
using Microsoft.EntityFrameworkCore;

namespace Repository;

public sealed class RankingRepository(RepositoryContext context) : IRankingRepository
{
    public Task<DateTimeOffset?> GetEarliestLikeActivityUtcAsync(CancellationToken cancellationToken = default)
        => context.ImageLikeActivities.AsNoTracking().OrderBy(activity => activity.OccurredAtUtc).Select(activity => (DateTimeOffset?)activity.OccurredAtUtc).FirstOrDefaultAsync(cancellationToken);

    public async Task<List<RankingScore>> GetLiveScoresAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken = default)
        => await context.ImageLikeActivities.AsNoTracking().Where(activity => activity.OccurredAtUtc >= startUtc && activity.OccurredAtUtc < endUtc)
            .GroupBy(activity => activity.ImageId).Select(group => new { ImageId = group.Key, LikeDelta = group.Sum(item => item.Type == LikeActivityType.Like ? 1 : -1) })
            .Where(item => item.LikeDelta > 0).Join(context.Images.AsNoTracking(), score => score.ImageId, image => image.Id,
                (score, image) => new RankingScore(image.Id, score.LikeDelta, image.UploadedAtUtc)).ToListAsync(cancellationToken);

    public Task<RankingSnapshot?> GetSnapshotAsync(RankingPeriod period, DateTimeOffset startUtc, bool trackChanges, CancellationToken cancellationToken = default)
        => (trackChanges ? context.RankingSnapshots : context.RankingSnapshots.AsNoTracking()).Include(snapshot => snapshot.Entries)
            .SingleOrDefaultAsync(snapshot => snapshot.Period == period && snapshot.PeriodStartUtc == startUtc, cancellationToken);
    public Task AddSnapshotAsync(RankingSnapshot snapshot, CancellationToken cancellationToken = default) => context.RankingSnapshots.AddAsync(snapshot, cancellationToken).AsTask();

    public async Task<List<(RankingSnapshotEntry Entry, AppImage Image)>> GetSnapshotEntriesAsync(int snapshotId, CancellationToken cancellationToken = default)
    {
        var entries = await context.RankingSnapshotEntries.AsNoTracking().Where(entry => entry.SnapshotId == snapshotId)
            .Join(context.Images.AsNoTracking().Include(image => image.Tags).Include(image => image.UploadedBy).Include(image => image.Likes).Include(image => image.Comments),
                entry => entry.ImageId, image => image.Id, (entry, image) => new { entry, image }).ToListAsync(cancellationToken);
        return entries.Select(item => (item.entry, item.image)).ToList();
    }

    public Task<List<AppImage>> GetDiscoverableImagesAsync(IEnumerable<int> imageIds, CancellationToken cancellationToken = default)
    {
        var ids = imageIds.Distinct().ToArray();
        return context.Images.AsNoTracking().Include(image => image.Tags).Include(image => image.UploadedBy).Include(image => image.Likes).Include(image => image.Comments)
            .Where(image => ids.Contains(image.Id) && image.DeletedAtUtc == null && image.Visibility == ImageVisibility.Gallery && image.ModerationStatus == ModerationStatus.Approved).ToListAsync(cancellationToken);
    }
}
