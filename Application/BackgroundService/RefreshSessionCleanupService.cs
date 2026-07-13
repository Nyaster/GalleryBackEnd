using Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Application.BackgroundService;

public sealed class RefreshSessionCleanupService(IServiceScopeFactory scopeFactory, ILogger<RefreshSessionCleanupService> logger) : Microsoft.Extensions.Hosting.BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var repositories = scope.ServiceProvider.GetRequiredService<IRepositoryManager>();
                await repositories.AppUser.PurgeExpiredRefreshSessionsAsync(TimeProvider.System.GetUtcNow(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Refresh-session cleanup failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
