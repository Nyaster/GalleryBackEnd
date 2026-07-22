using Application.Features.Images.GetImageContent;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using Moq;
using Service.Contracts;

namespace GallerySiteUnitTests;

public sealed class GetImageContentHandlerTests
{
    [Fact]
    public async Task Handle_JpegSourceRequestedAsJpeg_ReturnsOriginalWithoutConversion()
    {
        var image = Image("image/jpeg", uploadedById: 7);
        var repositories = Repositories(image);
        var storage = new Mock<IImageStorage>();
        storage.Setup(service => service.Exists(image.StorageKey)).Returns(true);
        storage.Setup(service => service.OpenReadAsync(image.StorageKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream([1, 2, 3]));
        var processor = new Mock<IImageProcessor>();
        var cache = new Mock<IImageDerivativeCache>();
        var handler = new Handler(repositories.Object, storage.Object, processor.Object, cache.Object, new TestUser(7));

        var result = await handler.Handle(new Command(image.Id, true), CancellationToken.None);

        Assert.Equal("image/jpeg", result.ContentType);
        await using (result.Stream)
            Assert.Equal([1, 2, 3], await ReadAsync(result.Stream));
        cache.Verify(service => service.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<ImageDerivativeVariant>(),
            It.IsAny<Func<Stream, CancellationToken, Task<Stream>>>(), It.IsAny<CancellationToken>()), Times.Never);
        processor.Verify(service => service.ConvertToJpegAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NonJpegSourceRequestedAsJpeg_UsesDerivativeCache()
    {
        var image = Image("image/png", uploadedById: 7);
        var repositories = Repositories(image);
        var storage = new Mock<IImageStorage>();
        storage.Setup(service => service.Exists(image.StorageKey)).Returns(true);
        var processor = new Mock<IImageProcessor>();
        var cache = new Mock<IImageDerivativeCache>();
        cache.Setup(service => service.GetOrCreateAsync(image.StorageKey, ImageDerivativeVariants.JpegV1,
                It.IsAny<Func<Stream, CancellationToken, Task<Stream>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream([4, 5]));
        var handler = new Handler(repositories.Object, storage.Object, processor.Object, cache.Object, new TestUser(7));

        var result = await handler.Handle(new Command(image.Id, true), CancellationToken.None);

        Assert.Equal("image/jpeg", result.ContentType);
        await using (result.Stream)
            Assert.Equal([4, 5], await ReadAsync(result.Stream));
        cache.Verify(service => service.GetOrCreateAsync(image.StorageKey, ImageDerivativeVariants.JpegV1,
            It.IsAny<Func<Stream, CancellationToken, Task<Stream>>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UnauthorizedImage_DoesNotAccessStorageOrCache()
    {
        var image = Image("image/png", uploadedById: 7);
        image.Visibility = ImageVisibility.Private;
        var storage = new Mock<IImageStorage>();
        var cache = new Mock<IImageDerivativeCache>();
        var handler = new Handler(Repositories(image).Object, storage.Object, new Mock<IImageProcessor>().Object,
            cache.Object, new TestUser(8));

        await Assert.ThrowsAsync<AppForbiddenException>(() => handler.Handle(new Command(image.Id, true), CancellationToken.None));

        storage.Verify(service => service.Exists(It.IsAny<string>()), Times.Never);
        cache.Verify(service => service.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<ImageDerivativeVariant>(),
            It.IsAny<Func<Stream, CancellationToken, Task<Stream>>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Mock<IRepositoryManager> Repositories(AppImage image)
    {
        var images = new Mock<IAppImageRepository>();
        images.Setup(repository => repository.GetByIdAsync(image.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(image);
        var repositories = new Mock<IRepositoryManager>();
        repositories.SetupGet(repository => repository.AppImage).Returns(images.Object);
        return repositories;
    }

    private static AppImage Image(string contentType, int uploadedById) => new UserMadeImage
    {
        Id = 42,
        UploadedById = uploadedById,
        UploadedAtUtc = DateTimeOffset.UtcNow,
        Visibility = ImageVisibility.Gallery,
        ModerationStatus = ModerationStatus.Pending,
        StorageKey = "uploads/source.png",
        ContentType = contentType,
        Width = 10,
        Height = 10
    };

    private static async Task<byte[]> ReadAsync(Stream stream)
    {
        using var output = new MemoryStream();
        await stream.CopyToAsync(output);
        return output.ToArray();
    }

    private sealed class TestUser(int userId) : IUserContext
    {
        public int? UserId => userId;
        public string? Login => "tester";
        public bool IsInRole(string role) => false;
        public void RequireAuthenticated() { }
    }
}
