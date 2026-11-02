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

    internal async Task CreateMissingSnapshotsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repositories = scope.ServiceProvider.GetRequiredService<IRepositoryManager>();
        var firstActivity = await repositories.Rankings.GetEarliestLikeActivityUtcAsync(cancellationToken);
        if (firstActivity is null) return;
        var existing = (await repositories.Rankings.GetSnapshotKeysAsync(cancellationToken)).ToHashSet();
        var now = clock.GetUtcNow();
        foreach (var period in Enum.GetValues<RankingPeriod>())
        {
            var start = RankingPeriodBounds.Current(period, firstActivity.Value).StartUtc;
            var currentStart = RankingPeriodBounds.Current(period, now).StartUtc;
            while (start < currentStart)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!existing.Contains(new RankingSnapshotKey(period, start)))
                    await repositories.Rankings.TryCreateSnapshotAsync(period, start,
                        RankingPeriodBounds.Next(period, start), now, cancellationToken);
                start = RankingPeriodBounds.Next(period, start);
            }
        }
    }
}
