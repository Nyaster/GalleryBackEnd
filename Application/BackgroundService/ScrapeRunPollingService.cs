using Contracts;
using Entities.Models;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Service.Contracts;

namespace Application.BackgroundService;

public sealed class ScrapeRunPollingService(IServiceScopeFactory scopeFactory, ILogger<ScrapeRunPollingService> logger) : Microsoft.Extensions.Hosting.BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                await ProcessOneAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Scrape-run poll failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessOneAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repositories = scope.ServiceProvider.GetRequiredService<IRepositoryManager>();
        var run = await repositories.ClaimNextScrapeRunAsync(TimeProvider.System.GetUtcNow(), cancellationToken);
        if (run is null) return;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await scope.ServiceProvider.GetRequiredService<IImageParserService>().RunAsync(run.Mode, cancellationToken);
            run.ImagesDiscovered = result.ImagesDiscovered;
            run.ImagesImported = result.ImagesImported;
            run.FailedItems = result.FailedItems;
            run.CompletedWithErrors = result.CompletedWithErrors;
            run.Status = BackgroundJobStatus.Completed;
            run.CompletedAtUtc = TimeProvider.System.GetUtcNow();
            logger.LogInformation("ScrapeRunCompleted {ScrapeRunId} {AttemptCount} {ElapsedMilliseconds} {ImagesDiscovered} {ImagesImported} {FailedItems}",
                run.Id, run.Attempts, Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2), run.ImagesDiscovered, run.ImagesImported, run.FailedItems);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            run.Status = BackgroundJobStatus.Cancelled;
            run.CompletedAtUtc = TimeProvider.System.GetUtcNow();
        }
        catch (Exception exception)
        {
            run.Status = BackgroundJobStatus.Failed;
            run.Error = exception.Message[..Math.Min(exception.Message.Length, 2048)];
            run.CompletedAtUtc = TimeProvider.System.GetUtcNow();
            logger.LogError(exception, "ScrapeRunFailed {ScrapeRunId} {AttemptCount} {ElapsedMilliseconds}", run.Id,
                run.Attempts, Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2));
        }
        await repositories.SaveAsync(cancellationToken);
    }
}
