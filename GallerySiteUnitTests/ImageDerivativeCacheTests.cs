using System.ComponentModel.DataAnnotations;
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

            await using (var first = await firstCache.GetOrCreateAsync("uploads/source.png", ImageDerivativeVariants.PreviewJpeg768V1,
                             (_, _) =>
                             {
                                 conversions++;
                                 return Task.FromResult<Stream>(new MemoryStream([4, 5, 6]));
                             }))
                Assert.Equal([4, 5, 6], await ReadAsync(first));

            var restartedCache = Cache(storage);
            await using (var second = await restartedCache.GetOrCreateAsync("uploads/source.png", ImageDerivativeVariants.PreviewJpeg768V1,
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
                .Select(_ => cache.GetOrCreateAsync("uploads/source.png", ImageDerivativeVariants.PreviewJpeg768V1, Convert))
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

            await using var firstV1 = await cache.GetOrCreateAsync("uploads/first.png", ImageDerivativeVariants.PreviewJpeg768V1,
                (_, _) => Task.FromResult<Stream>(new MemoryStream([1, 1])));
            await using var firstV2 = await cache.GetOrCreateAsync("uploads/first.png", jpegV2,
                (_, _) => Task.FromResult<Stream>(new MemoryStream([1, 2])));
            await using var secondV1 = await cache.GetOrCreateAsync("uploads/second.png", ImageDerivativeVariants.PreviewJpeg768V1,
                (_, _) => Task.FromResult<Stream>(new MemoryStream([2, 1])));

            Assert.Equal([1, 1], await ReadAsync(firstV1));
            Assert.Equal([1, 2], await ReadAsync(firstV2));
            Assert.Equal([2, 1], await ReadAsync(secondV1));
            await using var firstHit = await Preview(cache, "uploads/first.png");
            await using var secondHit = await Preview(cache, "uploads/second.png");
            Assert.Equal([1, 1], await ReadAsync(firstHit));
            Assert.Equal([2, 1], await ReadAsync(secondHit));
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

        await using var result = await cache.GetOrCreateAsync("uploads/source.png", ImageDerivativeVariants.PreviewJpeg768V1,
            (_, _) => Task.FromResult<Stream>(new MemoryStream([7, 7])));

        Assert.Equal([7, 7], await ReadAsync(result));
        storage.Invocations.Clear();
        await using var hit = await Preview(cache);
        Assert.Equal([7, 7], await ReadAsync(hit));
        storage.VerifyNoOtherCalls();
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

    [Fact]
    public async Task GetOrCreateAsync_WarmPreview_ReturnsIndependentReadOnlyStreamsWithoutStorageAccess()
    {
        var storage = DiskStorage([4, 5, 6]);
        using var cache = Cache(storage.Object);
        await using (var first = await Preview(cache))
            Assert.Equal([4, 5, 6], await ReadAsync(first));
        storage.Invocations.Clear();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Preview(cache)));
        Assert.Equal(responses.Length, responses.Distinct().Count());
        responses[0].Position = 2;
        await responses[0].DisposeAsync();
        foreach (var response in responses.Skip(1))
        {
            await using (response)
            {
                Assert.Equal(0, response.Position);
                Assert.False(response.CanWrite);
                Assert.False(Assert.IsType<MemoryStream>(response).TryGetBuffer(out _));
                Assert.Equal([4, 5, 6], await ReadAsync(response));
            }
        }
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetOrCreateAsync_GeneratedPreviewBufferChanges_CachedBytesRemainImmutable()
    {
        var bytes = new byte[] { 7, 8 };
        var storage = new Mock<IImageStorage>();
        storage.Setup(service => service.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream([1]));
        storage.Setup(service => service.SaveTemporaryAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Disk unavailable"));
        using var cache = Cache(storage.Object);
        await using var first = await cache.GetOrCreateAsync("source", ImageDerivativeVariants.PreviewJpeg768V1,
            (_, _) => Task.FromResult<Stream>(new MemoryStream(bytes)));
        bytes[0] = 99;
        await using var hit = await Preview(cache, "source");
        Assert.Equal([7, 8], await ReadAsync(first));
        Assert.Equal([7, 8], await ReadAsync(hit));
    }

    [Theory]
    [InlineData(0, 10, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 1048576, true)]
    [InlineData(1, 1048577, false)]
    public async Task GetOrCreateAsync_AdmissionBoundaries_PreserveBytesAndRespectBudget(
        int memoryMegabytes, int length, bool admitted)
    {
        var bytes = Enumerable.Repeat((byte)7, length).ToArray();
        var storage = DiskStorage(bytes);
        using var cache = Cache(storage.Object, memoryMegabytes);
        await using (var first = await Preview(cache))
            Assert.Equal(bytes, await ReadAsync(first));
        storage.Invocations.Clear();
        await using (var second = await Preview(cache))
            Assert.Equal(bytes, await ReadAsync(second));
        storage.Verify(service => service.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            admitted ? Times.Never : Times.Once);
    }

    [Fact]
    public async Task GetOrCreateAsync_FullJpegWithMemoryEnabled_AlwaysReadsDisk()
    {
        var storage = DiskStorage([1]);
        using var cache = Cache(storage.Object);
        for (var i = 0; i < 2; i++)
        {
            await using var response = await cache.GetOrCreateAsync("source", ImageDerivativeVariants.JpegV1,
                (_, _) => throw new InvalidOperationException());
            Assert.Equal([1], await ReadAsync(response));
        }
        storage.Verify(service => service.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetOrCreateAsync_WarmPreviewWithCanceledToken_Throws()
    {
        var storage = DiskStorage([1]);
        using var cache = Cache(storage.Object);
        await using var first = await Preview(cache);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Preview(cache, cancellationToken: cancellation.Token));
    }

    [Theory]
    [InlineData("io")]
    [InlineData("truncated")]
    [InlineData("canceled")]
    public async Task GetOrCreateAsync_AdmissionReadFails_DisposesCandidateAndAllowsRetry(string failure)
    {
        var candidate = new ObservedStream([1, 2, 3], (_, token) =>
        {
            if (failure == "io") throw new IOException("Read failed");
            if (failure == "canceled") throw new OperationCanceledException(token);
            return ValueTask.CompletedTask;
        }, truncated: failure == "truncated");
        var storage = DiskStorage([1, 2, 3]);
        storage.SetupSequence(service => service.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate).ReturnsAsync(new MemoryStream([1, 2, 3]));
        using var cache = Cache(storage.Object);
        var exception = await Record.ExceptionAsync(() => Preview(cache));
        if (failure == "canceled") Assert.IsAssignableFrom<OperationCanceledException>(exception);
        else Assert.IsAssignableFrom<IOException>(exception);
        Assert.True(candidate.Disposed);
        await using var retry = await Preview(cache);
        Assert.Equal([1, 2, 3], await ReadAsync(retry));
        storage.Verify(service => service.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetOrCreateAsync_ConcurrentDiskWarmingAndCanceledWaiter_AdmitsOnceAndReleasesGate()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var candidate = new ObservedStream([8, 9], async (_, token) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
        });
        var storage = DiskStorage([8, 9]);
        storage.Setup(service => service.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(candidate);
        using var cache = Cache(storage.Object);
        var first = Preview(cache);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var canceledWaiter = Preview(cache, cancellationToken: cancellation.Token);
        var others = Enumerable.Range(0, 6).Select(_ => Preview(cache)).ToArray();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWaiter);
        release.SetResult();
        var responses = await Task.WhenAll(others.Prepend(first));
        foreach (var response in responses)
            await using (response) Assert.Equal([8, 9], await ReadAsync(response));
        Assert.True(candidate.Disposed);
        storage.Verify(service => service.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrCreateAsync_TwoAdmissionsBusy_ThirdBypassesWithoutWaitingAndSlotsRecoverAfterCancellation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var candidates = new List<ObservedStream>();
        var storage = DiskStorage([3, 4]);
        storage.Setup(service => service.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var candidate = new ObservedStream([3, 4], async (_, token) =>
                {
                    if (Interlocked.Increment(ref count) == 2) started.TrySetResult();
                    await release.Task.WaitAsync(token);
                });
                candidates.Add(candidate);
                return candidate;
            });
        using var cache = Cache(storage.Object);
        using var cancellation = new CancellationTokenSource();
        var first = Preview(cache, "first", cancellation.Token);
        var second = Preview(cache, "second", cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var thirdTask = Preview(cache, "third");
        Assert.True(thirdTask.IsCompletedSuccessfully);
        await using (var third = await thirdTask)
        {
            Assert.Same(candidates[2], third);
            Assert.Equal(0, third.Position);
        }
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.All(candidates, candidate => Assert.True(candidate.Disposed));
        release.SetResult();
        await using (var retry = await Preview(cache, "third"))
            Assert.Equal([3, 4], await ReadAsync(retry));
        storage.Invocations.Clear();
        await using var hit = await Preview(cache, "third");
        Assert.Equal([3, 4], await ReadAsync(hit));
        storage.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetOrCreateAsync_NonseekableOrUnknownLengthDiskCandidate_BypassesMemory(bool unknownLength)
    {
        var storage = DiskStorage([1, 2]);
        storage.Setup(service => service.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ObservedStream([1, 2], seekable: unknownLength, unknownLength: unknownLength));
        using var cache = Cache(storage.Object);
        for (var i = 0; i < 2; i++)
        {
            await using var result = await Preview(cache);
            var bytes = new byte[2];
            await result.ReadExactlyAsync(bytes);
            Assert.Equal([1, 2], bytes);
        }
        storage.Verify(service => service.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetOrCreateAsync_CapacityPressure_PreservesBytesAndFallsBackToDisk()
    {
        var bytes = Enumerable.Repeat((byte)42, 600 * 1024).ToArray();
        var storage = DiskStorage(bytes);
        using var cache = Cache(storage.Object, 1);
        for (var pass = 0; pass < 2; pass++)
        {
            if (pass == 1) storage.Invocations.Clear();
            for (var index = 0; index < 4; index++)
            {
                await using var result = await Preview(cache, $"source-{index}");
                Assert.Equal(bytes, await ReadAsync(result));
            }
        }
        storage.Verify(service => service.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(128, true)]
    [InlineData(1024, true)]
    [InlineData(1025, false)]
    public void ImageStorageOptions_MemoryBudget_ValidatesRange(int megabytes, bool valid)
    {
        Assert.Equal(128, new ImageStorageOptions().PreviewMemoryCacheMegabytes);
        var options = new ImageStorageOptions { PreviewMemoryCacheMegabytes = megabytes };
        Assert.Equal(valid, Validator.TryValidateObject(options, new ValidationContext(options), [], true));
    }

    private sealed class ObservedStream(byte[] bytes,
        Func<Memory<byte>, CancellationToken, ValueTask>? beforeRead = null, bool truncated = false, bool seekable = true, bool unknownLength = false)
        : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        public override bool CanSeek => seekable;
        public override long Length => unknownLength ? throw new NotSupportedException() : base.Length;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (beforeRead is not null) await beforeRead(buffer, cancellationToken);
            return truncated ? 0 : await base.ReadAsync(buffer, cancellationToken);
        }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private static Task<Stream> Preview(LocalImageDerivativeCache cache, string key = "uploads/source.png",
        CancellationToken cancellationToken = default)
        => cache.GetOrCreateAsync(key, ImageDerivativeVariants.PreviewJpeg768V1,
            (_, _) => throw new InvalidOperationException("Disk preview must already exist."), cancellationToken);

    private static Mock<IImageStorage> DiskStorage(byte[] bytes)
    {
        var storage = new Mock<IImageStorage>(MockBehavior.Strict);
        storage.Setup(service => service.Exists(It.IsAny<string>())).Returns(true);
        storage.Setup(service => service.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(bytes));
        return storage;
    }

    private static LocalImageDerivativeCache Cache(IImageStorage storage, int memoryMegabytes = 128)
        => new(storage, NullLogger<LocalImageDerivativeCache>.Instance,
            Options.Create(new ImageStorageOptions { PreviewMemoryCacheMegabytes = memoryMegabytes }));

    private static LocalImageStorage Storage(string root)
        => new(Options.Create(new ImageStorageOptions { RootPath = root }), NullLogger<LocalImageStorage>.Instance);

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
