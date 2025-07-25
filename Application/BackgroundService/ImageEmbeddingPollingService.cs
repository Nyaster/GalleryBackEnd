using Application.Features.Images.GenerateImageEmbedding;
using Contracts;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Application.BackgroundService;

public class ImageEmbeddingPollingService : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PeriodicTimer _timer = new(TimeSpan.FromMinutes(2));
    private readonly int _batchSize = 10;

    public ImageEmbeddingPollingService(IServiceScopeFactory scopeFactory)
    {
        var FromMinutes = Environment.GetEnvironmentVariable("GENERATE_EVERY") ?? "2";
        var batch = Environment.GetEnvironmentVariable("GENERATE_SIZE") ?? "10";
        _timer = new PeriodicTimer(TimeSpan.FromMinutes(int.Parse(FromMinutes)));
        _batchSize = int.Parse(batch);
        _scopeFactory = scopeFactory;
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await _timer.WaitForNextTickAsync(stoppingToken) && !stoppingToken.IsCancellationRequested)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var dbImages = scope.ServiceProvider.GetRequiredService<IRepositoryManager>();
            int batchSize = _batchSize;
            var findImageByCondition = await dbImages.AppImage.FindImageByCondition(x => x.Embedding == null, false);
            var imageIdsToProcess =
                await findImageByCondition.Where(x=>x.IsHidden == false)
                    .OrderByDescending(x => x.Id).Take(batchSize).Select(x => x.Id)
                    .ToListAsync(cancellationToken: stoppingToken);
            if (imageIdsToProcess.Count == 0)
            {
                continue;
            }

            var processingTasks = imageIdsToProcess.Select(async id =>
            {
                // Create a new scope for each parallel task
                await using var serviceScope = _scopeFactory.CreateAsyncScope();
                var mediatorInstance = serviceScope.ServiceProvider.GetRequiredService<IMediator>();
                await mediatorInstance.Send(new Command(id), stoppingToken);
            });

            await Task.WhenAll(processingTasks);
        }
    }
}