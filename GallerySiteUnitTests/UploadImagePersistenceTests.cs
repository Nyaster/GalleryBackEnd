using Contracts;
using Entities.Exceptions;
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
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    public async Task UploadImage_PersistenceOrResponseReadFails_OnlyDeletesUncommittedFile(bool saved, bool committed, bool trusted)
    {
        using var files = new ImageStagingTestFiles();
        AppImage? addedImage = null;
        var images = new Mock<IAppImageRepository>();
        images.Setup(repository => repository.GetOrCreateTagsAsync(It.IsAny<IEnumerable<string>>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        images.Setup(repository => repository.AddAsync(It.IsAny<AppImage>(), It.IsAny<CancellationToken>()))
            .Callback((AppImage image, CancellationToken _) => addedImage = image)
            .Returns(Task.CompletedTask);
        images.Setup(repository => repository.GetManagementCardByIdAsync(It.IsAny<int>(), It.IsAny<ImageViewer>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Response read failed."));
        var users = new Mock<IAppUserRepository>();
        users.Setup(repository => repository.GetByIdAsync(7, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UploadAccount(blocked: false));
        var repositories = new Mock<IRepositoryManager>();
        repositories.SetupGet(repository => repository.AppUser).Returns(users.Object);
        repositories.SetupGet(repository => repository.AppImage).Returns(images.Object);
        var transaction = new Mock<IRepositoryTransaction>();
        repositories.Setup(repository => repository.BeginSerializableTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction.Object);
        if (committed)
            transaction.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        else
            transaction.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("Commit failed."));
        if (saved)
            repositories.Setup(repository => repository.SaveAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        else
            repositories.Setup(repository => repository.SaveAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("Save failed."));
        var file = new Mock<IFormFile>();
        file.SetupGet(value => value.Length).Returns(1);
        file.Setup(value => value.OpenReadStream()).Returns(() => new MemoryStream([1]));
        var handler = new Application.Features.Images.UploadImage.Handler(repositories.Object, files.Stager,
            new Uploader(trusted), TimeProvider.System, Options.Create(new ImageStorageOptions()));

        await Assert.ThrowsAsync<IOException>(() => handler.Handle(new(new AppImageCreationDto
            { ImageFile = file.Object, AiUsage = AiUsageClassification.HumanMade }), CancellationToken.None));

        Assert.NotNull(addedImage);
        Assert.Equal(trusted ? ModerationStatus.Approved : ModerationStatus.Pending, addedImage.ModerationStatus);
        Assert.Equal(committed, files.Storage.Exists(addedImage.StorageKey));
        Assert.Matches(@"^uploads/[a-f0-9]{32}\.png$", addedImage.StorageKey);
        Assert.Equal(ImageStagingTestFiles.Metadata.ContentType, addedImage.ContentType);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(files.Root, ".tmp")));
        images.Verify(repository => repository.GetManagementCardByIdAsync(It.IsAny<int>(), It.IsAny<ImageViewer>(), It.IsAny<CancellationToken>()),
            committed ? Times.Once() : Times.Never());
        transaction.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), saved ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task UploadImage_BlockedDuringStaging_DoesNotPublishTrustedUpload()
    {
        using var files = new ImageStagingTestFiles();
        var users = new Mock<IAppUserRepository>();
        users.SetupSequence(repository => repository.GetByIdAsync(7, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UploadAccount(blocked: false))
            .ReturnsAsync(UploadAccount(blocked: true));
        var repositories = new Mock<IRepositoryManager>();
        repositories.SetupGet(repository => repository.AppUser).Returns(users.Object);
        repositories.Setup(repository => repository.BeginSerializableTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IRepositoryTransaction>());
        var file = new Mock<IFormFile>();
        file.SetupGet(value => value.Length).Returns(1);
        file.Setup(value => value.OpenReadStream()).Returns(() => new MemoryStream([1]));
        var handler = new Application.Features.Images.UploadImage.Handler(repositories.Object, files.Stager,
            new Uploader(trusted: true), TimeProvider.System, Options.Create(new ImageStorageOptions()));

        await Assert.ThrowsAsync<AppForbiddenException>(() => handler.Handle(new(new AppImageCreationDto
            { ImageFile = file.Object, AiUsage = AiUsageClassification.HumanMade }), CancellationToken.None));

        repositories.VerifyGet(repository => repository.AppImage, Times.Never);
        repositories.Verify(repository => repository.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(Directory.EnumerateFiles(files.Root, "*", SearchOption.AllDirectories));
    }

    private static AppUser UploadAccount(bool blocked) => new()
    {
        Id = 7,
        Login = "uploader",
        NormalizedLogin = "UPLOADER",
        PasswordHash = "unused",
        CanUploadImages = true,
        UploadsBlocked = blocked
    };

    private sealed class Uploader(bool trusted) : IUserContext
    {
        public int? UserId => 7;
        public string? Login => "uploader";
        public bool HasPermission(string permission) => trusted && permission == AccessPermission.UploadsBypassReview;
        public void RequireAuthenticated() { }
    }
}
