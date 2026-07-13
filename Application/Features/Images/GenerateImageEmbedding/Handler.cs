using Contracts;
using System.Diagnostics;
using Entities.Models;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Service;
using Service.Contracts;

namespace Application.Features.Images.GenerateImageEmbedding;

public sealed class Handler(IRepositoryManager repositories, IImageStorage storage, IImageEmbeddingGenerator generator,
    IOptions<EmbeddingOptions> options, ILogger<Handler> logger)
    : IRequestHandler<Command>
{
    public async Task Handle(Command request, CancellationToken cancellationToken)
    {
        var image = await repositories.AppImage.GetByIdAsync(request.ImageId, true, cancellationToken);
        if (image is null || image.EmbeddingStatus != EmbeddingStatus.Processing) return;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (!storage.Exists(image.StorageKey)) throw new FileNotFoundException("Image file does not exist.");
            await using var stream = await storage.OpenReadAsync(image.StorageKey, cancellationToken);
            image.Embedding = await generator.GenerateEmbeddingAsync(stream, cancellationToken);
            image.EmbeddingStatus = EmbeddingStatus.Ready;
            image.EmbeddingError = null;
            logger.LogInformation("EmbeddingGenerationCompleted {ImageId} {AttemptCount} {ElapsedMilliseconds}", image.Id,
                image.EmbeddingAttempts, Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            image.EmbeddingStatus = image.EmbeddingAttempts >= options.Value.MaxAttempts ? EmbeddingStatus.Failed : EmbeddingStatus.Pending;
            image.EmbeddingError = exception.Message[..Math.Min(exception.Message.Length, 1024)];
            logger.LogError(exception, "EmbeddingGenerationFailed {ImageId} {AttemptCount} {ElapsedMilliseconds}", image.Id,
                image.EmbeddingAttempts, Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2));
        }
        image.EmbeddingLeaseExpiresAtUtc = null;
        await repositories.SaveAsync(cancellationToken);
    }
}
