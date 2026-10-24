using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Service;
using Service.Contracts;

namespace GallerySiteUnitTests;

internal sealed class ImageStagingTestFiles : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"gallery-staging-{Guid.NewGuid():N}");
    public LocalImageStorage Storage { get; }
    public Mock<IImageProcessor> Processor { get; } = new();
    public ImageStager Stager { get; }
    public static InspectedImage Metadata { get; } = new(10, 20, "image/png", ".png");

    public ImageStagingTestFiles()
    {
        Storage = new LocalImageStorage(Options.Create(new ImageStorageOptions { RootPath = Root }),
            NullLogger<LocalImageStorage>.Instance);
        Processor.Setup(processor => processor.InspectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Metadata);
        Stager = new ImageStager(Storage, Processor.Object, NullLogger<ImageStager>.Instance);
    }

    public string FullPath(string storageKey) => Path.Combine(Root, storageKey);

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}
