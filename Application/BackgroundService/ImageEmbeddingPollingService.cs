using Application.Features.Images.GenerateImageEmbedding;
using Contracts;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Service;

namespace Application.BackgroundService;

public sealed class ImageEmbeddingPollingService(
    IServiceScopeFactory scopeFactory,
    IOptions<EmbeddingOptions> options,
    ILogger<ImageEmbeddingPollingService> logger) : Microsoft.Extensions.Hosting.BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.Value.PollSeconds);
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Image embedding poll failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repositories = scope.ServiceProvider.GetRequiredService<IRepositoryManager>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var ids = await repositories.AppImage.ClaimPendingEmbeddingsAsync(options.Value.BatchSize, TimeProvider.System.GetUtcNow(), cancellationToken);
        foreach (var id in ids)
            await mediator.Send(new Command(id), cancellationToken);
    }
}
