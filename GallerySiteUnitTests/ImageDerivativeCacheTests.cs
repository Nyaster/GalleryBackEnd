using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Service;
using Service.Contracts;

namespace GallerySiteUnitTests;

public sealed class ImageDerivativeCacheTests
{
    [Fact]
    public async Task GetOrCreateAsync_CacheMissPersistsDerivative_NewInstanceUsesCache()
    {
        var root = TemporaryRoot();
        try
        {
            var storage = Storage(root);
            await SaveAsync(storage, "uploads/source.png", [1, 2, 3]);
            var conversions = 0;
            var firstCache = Cache(storage);

            await using (var first = await firstCache.GetOrCreateAsync("uploads/source.png", ImageDerivativeVariants.JpegV1,
                             (_, _) =>
                             {
                                 conversions++;
                                 return Task.FromResult<Stream>(new MemoryStream([4, 5, 6]));
                             }))
                Assert.Equal([4, 5, 6], await ReadAsync(first));

            var restartedCache = Cache(storage);
            await using (var second = await restartedCache.GetOrCreateAsync("uploads/source.png", ImageDerivativeVariants.JpegV1,
                             (_, _) => throw new InvalidOperationException("A cache hit must not run the converter.")))
                Assert.Equal([4, 5, 6], await ReadAsync(second));

            Assert.Equal(1, conversions);
            firstCache.Dispose();
            restartedCache.Dispose();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task GetOrCreateAsync_ConcurrentMisses_ConvertsOnce()
    {
        var root = TemporaryRoot();
        try
        {
            var storage = Storage(root);
            await SaveAsync(storage, "uploads/source.png", [1]);
            using var cache = Cache(storage);
            var conversionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowConversion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var conversions = 0;

            async Task<Stream> Convert(Stream _, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref conversions);
                conversionStarted.SetResult();
                await allowConversion.Task.WaitAsync(cancellationToken);
                return new MemoryStream([9, 8, 7]);
            }

            var requests = Enumerable.Range(0, 5)
                .Select(_ => cache.GetOrCreateAsync("uploads/source.png", ImageDerivativeVariants.JpegV1, Convert))
                .ToArray();
            await conversionStarted.Task;
            allowConversion.SetResult();
            var results = await Task.WhenAll(requests);

            Assert.Equal(1, conversions);
            foreach (var result in results)
            {
                await using (result)
                    Assert.Equal([9, 8, 7], await ReadAsync(result));
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task GetOrCreateAsync_DifferentSourcesAndVariants_UseDistinctEntries()
    {
        var root = TemporaryRoot();
        try
        {
            var storage = Storage(root);
            await SaveAsync(storage, "uploads/first.png", [1]);
            await SaveAsync(storage, "uploads/second.png", [2]);
            using var cache = Cache(storage);
            var jpegV2 = new ImageDerivativeVariant("jpeg-v2", ".jpg");

            await using var firstV1 = await cache.GetOrCreateAsync("uploads/first.png", ImageDerivativeVariants.JpegV1,
                (_, _) => Task.FromResult<Stream>(new MemoryStream([1, 1])));
            await using var firstV2 = await cache.GetOrCreateAsync("uploads/first.png", jpegV2,
                (_, _) => Task.FromResult<Stream>(new MemoryStream([1, 2])));
            await using var secondV1 = await cache.GetOrCreateAsync("uploads/second.png", ImageDerivativeVariants.JpegV1,
                (_, _) => Task.FromResult<Stream>(new MemoryStream([2, 1])));

            Assert.Equal([1, 1], await ReadAsync(firstV1));
            Assert.Equal([1, 2], await ReadAsync(firstV2));
            Assert.Equal([2, 1], await ReadAsync(secondV1));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task GetOrCreateAsync_CacheWriteFails_ReturnsGeneratedStream()
    {
        var storage = new Mock<IImageStorage>();
        storage.Setup(service => service.Exists(It.IsAny<string>())).Returns(false);
        storage.Setup(service => service.OpenReadAsync("uploads/source.png", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream([1]));
        storage.Setup(service => service.SaveTemporaryAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Disk unavailable."));
        using var cache = Cache(storage.Object);

        await using var result = await cache.GetOrCreateAsync("uploads/source.png", ImageDerivativeVariants.JpegV1,
            (_, _) => Task.FromResult<Stream>(new MemoryStream([7, 7])));

        Assert.Equal([7, 7], await ReadAsync(result));
    }

    [Fact]
    public async Task GetOrCreateAsync_CacheWriteIsCanceled_PropagatesCancellation()
    {
        var storage = new Mock<IImageStorage>();
        storage.Setup(service => service.Exists(It.IsAny<string>())).Returns(false);
        storage.Setup(service => service.OpenReadAsync("uploads/source.png", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream([1]));
        storage.Setup(service => service.SaveTemporaryAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        using var cache = Cache(storage.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => cache.GetOrCreateAsync("uploads/source.png",
            ImageDerivativeVariants.JpegV1, (_, _) => Task.FromResult<Stream>(new MemoryStream([7, 7]))));
    }

    private static LocalImageDerivativeCache Cache(IImageStorage storage)
        => new(storage, NullLogger<LocalImageDerivativeCache>.Instance);

    private static LocalImageStorage Storage(string root)
        => new(Options.Create(new ImageStorageOptions { RootPath = root }));

    private static string TemporaryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gallery-derivative-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task SaveAsync(IImageStorage storage, string storageKey, byte[] bytes)
    {
        await using var source = new MemoryStream(bytes);
        var temporaryPath = await storage.SaveTemporaryAsync(source);
        await storage.MoveTemporaryToFinalAsync(temporaryPath, storageKey);
    }

    private static async Task<byte[]> ReadAsync(Stream stream)
    {
        using var output = new MemoryStream();
        await stream.CopyToAsync(output);
        return output.ToArray();
    }
}
