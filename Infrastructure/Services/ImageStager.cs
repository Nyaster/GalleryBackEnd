using Microsoft.Extensions.Logging;
using Service.Contracts;

namespace Service;

public sealed class ImageStager(IImageStorage storage, IImageProcessor processor, ILogger<ImageStager> logger)
    : IImageStager
{
    public async Task<IStagedImage> StageAsync(Stream source, string storageKeyPrefix,
        CancellationToken cancellationToken = default)
    {
        var temporaryPath = await storage.SaveTemporaryAsync(source, cancellationToken);
        try
        {
            var metadata = await processor.InspectAsync(temporaryPath, cancellationToken);
            var storageKey = $"{storageKeyPrefix}{Guid.NewGuid():N}{metadata.Extension}";
            var staged = new StagedImage(storage, storageKey, metadata, logger);
            await storage.MoveTemporaryToFinalAsync(temporaryPath, storageKey, cancellationToken);
            return staged;
        }
        catch
        {
            try
            {
                await storage.DeleteTemporaryAsync(temporaryPath, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "StagedImageTemporaryCleanupFailed {TemporaryPath}", temporaryPath);
            }
            throw;
        }
    }

    private sealed class StagedImage(IImageStorage storage, string storageKey, InspectedImage metadata,
        ILogger logger) : IStagedImage
    {
        private bool _persisted;
        private bool _disposed;

        public string StorageKey => storageKey;
        public InspectedImage Metadata => metadata;

        public void MarkPersisted() => _persisted = true;

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            if (_persisted) return;
            try
            {
                await storage.DeleteAsync(storageKey, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "StagedImageCleanupFailed {StorageKey}", storageKey);
            }
        }
    }
}
