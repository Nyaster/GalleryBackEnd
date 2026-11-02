using Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.BackgroundService;

public sealed class ForMeFeedCleanupService(IServiceScopeFactory scopeFactory, TimeProvider clock,
    ILogger<ForMeFeedCleanupService> logger) : Microsoft.Extensions.Hosting.BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10), clock);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var feeds = scope.ServiceProvider.GetRequiredService<IForMeFeedRepository>();
                while (await feeds.DeleteExpiredAsync(stoppingToken) == 1000)
                    stoppingToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Recommendation feed cleanup failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
