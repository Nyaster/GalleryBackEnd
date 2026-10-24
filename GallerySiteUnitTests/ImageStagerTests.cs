using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Service;
using Service.Contracts;

namespace GallerySiteUnitTests;

public sealed class ImageStagerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StageAsync_FileDisposed_OnlyRetainsPersistedFile(bool persisted)
    {
        using var files = new ImageStagingTestFiles();
        await using var source = new MemoryStream([1, 2, 3]);
        var staged = await files.Stager.StageAsync(source, "uploads/");

        Assert.Equal(ImageStagingTestFiles.Metadata, staged.Metadata);
        Assert.Matches(@"^uploads/[a-f0-9]{32}\.png$", staged.StorageKey);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(files.FullPath(staged.StorageKey)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(files.Root, ".tmp")));
        if (persisted) staged.MarkPersisted();
        await staged.DisposeAsync();

        Assert.Equal(persisted, files.Storage.Exists(staged.StorageKey));
        Assert.True(source.CanRead);
    }

    [Theory]
    [InlineData("copy")]
    [InlineData("inspect")]
    [InlineData("move")]
    public async Task StageAsync_PreparationFails_RemovesTemporaryFileAndPreservesExistingDestination(string failure)
    {
        using var files = new ImageStagingTestFiles();
        var original = new IOException("Preparation failed.");
        await using var source = failure == "copy"
            ? new PartialCopyStream(original)
            : new MemoryStream([1, 2, 3]);
        var storage = ForwardTemporaryStorage(files);
        string? existingDestination = null;
        if (failure == "inspect")
            files.Processor.Setup(processor => processor.InspectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(original);
        storage.Setup(service => service.MoveTemporaryToFinalAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string temporaryPath, string key, CancellationToken token) =>
            {
                existingDestination = files.FullPath(key);
                Directory.CreateDirectory(Path.GetDirectoryName(existingDestination)!);
                File.WriteAllText(existingDestination, "existing destination");
                return files.Storage.MoveTemporaryToFinalAsync(temporaryPath, key, token);
            });
        var stager = new ImageStager(storage.Object, files.Processor.Object, NullLogger<ImageStager>.Instance);

        var thrown = await Assert.ThrowsAsync<IOException>(() => stager.StageAsync(source, "uploads/"));

        if (failure != "move") Assert.Same(original, thrown);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(files.Root, ".tmp")));
        if (existingDestination is not null)
            Assert.Equal("existing destination", await File.ReadAllTextAsync(existingDestination));
        storage.Verify(service => service.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StageAsync_RequestCancelledDuringInspection_CleansUpAndPreservesOriginalFailure()
    {
        using var files = new ImageStagingTestFiles();
        using var cancellation = new CancellationTokenSource();
        var original = new IOException("Inspection failed after cancellation.");
        files.Processor.Setup(processor => processor.InspectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken _) =>
            {
                cancellation.Cancel();
                return Task.FromException<InspectedImage>(original);
            });
        await using var source = new MemoryStream([1]);

        var thrown = await Assert.ThrowsAsync<IOException>(() =>
            files.Stager.StageAsync(source, "uploads/", cancellation.Token));

        Assert.Same(original, thrown);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(files.Root, ".tmp")));
    }

    [Fact]
    public async Task DisposeAsync_RequestCancelled_DeletesUnpersistedFileOnceWithoutCancellation()
    {
        using var files = new ImageStagingTestFiles();
        using var cancellation = new CancellationTokenSource();
        var storage = ForwardTemporaryStorage(files);
        storage.Setup(service => service.MoveTemporaryToFinalAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string path, string key, CancellationToken token) => files.Storage.MoveTemporaryToFinalAsync(path, key, token));
        storage.Setup(service => service.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string key, CancellationToken token) => files.Storage.DeleteAsync(key, token));
        var stager = new ImageStager(storage.Object, files.Processor.Object, NullLogger<ImageStager>.Instance);
        await using var source = new MemoryStream([1]);
        var staged = await stager.StageAsync(source, "uploads/", cancellation.Token);
        cancellation.Cancel();

        await staged.DisposeAsync();
        await staged.DisposeAsync();

        Assert.False(files.Storage.Exists(staged.StorageKey));
        storage.Verify(service => service.DeleteAsync(staged.StorageKey, CancellationToken.None), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StageAsync_CleanupFails_LogsWarningAndPreservesOriginalFailure(bool finalFile)
    {
        var original = new IOException("Operation failed.");
        var cleanupFailure = new UnauthorizedAccessException("Deletion failed.");
        var storage = new Mock<IImageStorage>();
        storage.Setup(service => service.SaveTemporaryAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/temporary/image.upload");
        storage.Setup(service => service.MoveTemporaryToFinalAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        storage.Setup(service => service.DeleteTemporaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(cleanupFailure);
        storage.Setup(service => service.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(cleanupFailure);
        var processor = new Mock<IImageProcessor>();
        if (finalFile)
            processor.Setup(service => service.InspectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ImageStagingTestFiles.Metadata);
        else
            processor.Setup(service => service.InspectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(original);
        var logger = new Mock<ILogger<ImageStager>>();
        var stager = new ImageStager(storage.Object, processor.Object, logger.Object);
        await using var source = new MemoryStream([1]);

        var thrown = await Assert.ThrowsAsync<IOException>(async () =>
        {
            await using var staged = await stager.StageAsync(source, "uploads/");
            throw original;
        });

        Assert.Same(original, thrown);
        logger.Verify(value => value.Log(LogLevel.Warning, It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(), cleanupFailure,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    private static Mock<IImageStorage> ForwardTemporaryStorage(ImageStagingTestFiles files)
    {
        var storage = new Mock<IImageStorage>();
        storage.Setup(service => service.SaveTemporaryAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns((Stream source, CancellationToken token) => files.Storage.SaveTemporaryAsync(source, token));
        storage.Setup(service => service.DeleteTemporaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string path, CancellationToken token) => files.Storage.DeleteTemporaryAsync(path, token));
        return storage;
    }

    private sealed class PartialCopyStream(Exception failure) : MemoryStream
    {
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            await destination.WriteAsync(new byte[] { 1, 2, 3 }, cancellationToken);
            throw failure;
        }
    }
}
