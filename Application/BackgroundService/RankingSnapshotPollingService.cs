using Contracts;
using Entities.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Application.BackgroundService;

public sealed class RankingSnapshotPollingService(IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<RankingSnapshotPollingService> logger)
    : Microsoft.Extensions.Hosting.BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try { await CreateMissingSnapshotsAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Ranking snapshot poll failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CreateMissingSnapshotsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repositories = scope.ServiceProvider.GetRequiredService<IRepositoryManager>();
        var firstActivity = await repositories.Rankings.GetEarliestLikeActivityUtcAsync(cancellationToken);
        if (firstActivity is null) return;
        var now = clock.GetUtcNow();
        foreach (var period in Enum.GetValues<RankingPeriod>())
        {
            var start = RankingPeriodBounds.Current(period, firstActivity.Value).StartUtc;
            var currentStart = RankingPeriodBounds.Current(period, now).StartUtc;
            while (start < currentStart)
            {
                if (await repositories.Rankings.GetSnapshotAsync(period, start, false, cancellationToken) is null)
                    await CreateSnapshotAsync(repositories, period, start, cancellationToken);
                start = RankingPeriodBounds.Next(period, start);
            }
        }
    }

    private async Task CreateSnapshotAsync(IRepositoryManager repositories, RankingPeriod period, DateTimeOffset start, CancellationToken cancellationToken)
    {
        var end = RankingPeriodBounds.Next(period, start);
        var scores = await repositories.Rankings.GetLiveScoresAsync(start, end, cancellationToken);
        var images = await repositories.Rankings.GetDiscoverableImagesAsync(scores.Select(score => score.ImageId), cancellationToken);
        var visible = images.ToDictionary(image => image.Id);
        var entries = scores.Where(score => visible.ContainsKey(score.ImageId)).OrderByDescending(score => score.LikeDelta)
            .ThenByDescending(score => score.UploadedAtUtc).ThenByDescending(score => score.ImageId).Select((score, index) => new RankingSnapshotEntry
            { ImageId = score.ImageId, Rank = index + 1, LikeDelta = score.LikeDelta }).ToList();
        await repositories.Rankings.AddSnapshotAsync(new RankingSnapshot { Period = period, PeriodStartUtc = start, PeriodEndUtc = end, CreatedAtUtc = clock.GetUtcNow(), Entries = entries }, cancellationToken);
        await repositories.SaveAsync(cancellationToken);
    }
}
