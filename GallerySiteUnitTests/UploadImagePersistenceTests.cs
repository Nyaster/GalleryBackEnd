using Contracts;
using Entities.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using Service;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace GallerySiteUnitTests;

public sealed class UploadImagePersistenceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UploadImage_PersistenceOrResponseReadFails_OnlyDeletesUncommittedFile(bool saved)
    {
        var images = new Mock<IAppImageRepository>();
        images.Setup(repository => repository.GetOrCreateTagsAsync(It.IsAny<IEnumerable<string>>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        images.Setup(repository => repository.AddAsync(It.IsAny<AppImage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        images.Setup(repository => repository.GetCardByIdAsync(It.IsAny<int>(), It.IsAny<ImageViewer>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Response read failed."));
        var repositories = new Mock<IRepositoryManager>();
        repositories.SetupGet(repository => repository.AppImage).Returns(images.Object);
        if (saved)
            repositories.Setup(repository => repository.SaveAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        else
            repositories.Setup(repository => repository.SaveAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("Save failed."));
        var storage = new Mock<IImageStorage>();
        storage.Setup(service => service.SaveTemporaryAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/tmp/gallery-mocked-upload.upload");
        storage.Setup(service => service.MoveTemporaryToFinalAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        storage.Setup(service => service.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var processor = new Mock<IImageProcessor>();
        processor.Setup(service => service.InspectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InspectedImage(10, 10, "image/jpeg", ".jpg"));
        var file = new Mock<IFormFile>();
        file.SetupGet(value => value.Length).Returns(1);
        file.Setup(value => value.OpenReadStream()).Returns(() => new MemoryStream([1]));
        var handler = new Application.Features.Images.UploadImage.Handler(repositories.Object, storage.Object,
            processor.Object, new Admin(), TimeProvider.System, Options.Create(new ImageStorageOptions()));

        await Assert.ThrowsAsync<IOException>(() => handler.Handle(new(new AppImageCreationDto
            { ImageFile = file.Object, AiUsage = AiUsageClassification.HumanMade }), CancellationToken.None));

        storage.Verify(service => service.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            saved ? Times.Never() : Times.Once());
        images.Verify(repository => repository.GetCardByIdAsync(It.IsAny<int>(), It.IsAny<ImageViewer>(), It.IsAny<CancellationToken>()),
            saved ? Times.Once() : Times.Never());
    }

    private sealed class Admin : IUserContext
    {
        public int? UserId => 7;
        public string? Login => "admin";
        public bool IsInRole(string role) => role == "Admin";
        public void RequireAuthenticated() { }
    }
}
