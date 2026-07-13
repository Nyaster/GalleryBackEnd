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
        if (run.Attempts > 1)
        {
            run.TotalPages = 0;
            run.ScannedPages = 0;
            run.ImagesDiscovered = 0;
            run.EligibleCandidates = 0;
            run.PlannedDownloads = 0;
            run.ProcessedDownloads = 0;
            run.ImagesImported = 0;
            run.FailedItems = 0;
            run.CompletedWithErrors = false;
            run.Error = null;
        }
        using var logScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["ScrapeRunId"] = run.Id,
            ["Attempt"] = run.Attempts,
            ["Mode"] = run.Mode.ToString(),
            ["MaxImages"] = run.MaxImages
        });
        logger.LogInformation("Scrape run claimed and started");
        try
        {
            await scope.ServiceProvider.GetRequiredService<IImageParserService>().RunAsync(run, cancellationToken);
            run.Status = BackgroundJobStatus.Completed;
            run.CompletedAtUtc = TimeProvider.System.GetUtcNow();
            logger.LogInformation("Scrape run completed {ElapsedMilliseconds} {ImagesDiscovered} {EligibleCandidates} {PlannedDownloads} {ProcessedDownloads} {ImagesImported} {FailedItems}",
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2), run.ImagesDiscovered, run.EligibleCandidates,
                run.PlannedDownloads, run.ProcessedDownloads, run.ImagesImported, run.FailedItems);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            run.Status = BackgroundJobStatus.Cancelled;
            run.CompletedAtUtc = TimeProvider.System.GetUtcNow();
            logger.LogInformation("Scrape run cancelled {ElapsedMilliseconds}", Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2));
        }
        catch (Exception exception)
        {
            run.Status = BackgroundJobStatus.Failed;
            run.Error = $"Scrape run failed with {exception.GetType().Name}.";
            run.CompletedAtUtc = TimeProvider.System.GetUtcNow();
            logger.LogError("Scrape run failed {ElapsedMilliseconds} {FailureType}",
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2), exception.GetType().Name);
        }
        await repositories.SaveAsync(CancellationToken.None);
    }
}
