using Application.Features.Administration.ChangeModeration;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using Moq;
using Service.Contracts;

namespace GallerySiteUnitTests;

public sealed class ImageModerationPermissionTests
{
    [Theory]
    [InlineData(AccessPermission.ImagesModerate)]
    [InlineData(AccessPermission.ImagesClassifyAi)]
    public async Task ChangeModeration_OnlyOnePermission_RejectsRequestForBothFields(string permission)
    {
        var repositories = new Mock<IRepositoryManager>(MockBehavior.Strict);
        var handler = new Handler(repositories.Object, UserWith(permission));

        await Assert.ThrowsAsync<AppForbiddenException>(() => handler.Handle(
            new Command(42, ModerationStatus.Approved, AiUsageClassification.HumanMade), CancellationToken.None));
    }

    [Fact]
    public async Task ChangeModeration_ClassificationOnly_PreservesExistingStatus()
    {
        var image = new UserMadeImage
        {
            Id = 42, UploadedById = 8, StorageKey = "uploads/test.jpg", ContentType = "image/jpeg",
            Visibility = ImageVisibility.Gallery, ModerationStatus = ModerationStatus.Approved,
            AiUsage = AiUsageClassification.Unknown
        };
        var images = new Mock<IAppImageRepository>();
        images.Setup(value => value.GetByIdAsync(image.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync(image);
        images.Setup(value => value.GetCardByIdAsync(image.Id, It.IsAny<ImageViewer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ImageReadTestData.Card(image));
        var repositories = new Mock<IRepositoryManager>();
        repositories.SetupGet(value => value.AppImage).Returns(images.Object);
        repositories.Setup(value => value.BeginSerializableTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IRepositoryTransaction>());
        var handler = new Handler(repositories.Object, UserWith(AccessPermission.ImagesClassifyAi));

        var result = await handler.Handle(new Command(image.Id, AiUsage: AiUsageClassification.AiAssisted),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ModerationStatus.Approved, result.ModerationStatus);
        Assert.Equal(AiUsageClassification.AiAssisted, result.AiUsage);
        Assert.False(result.CanHide);
        Assert.Equal("/api/images/42/content", result.ContentUrl);
    }

    private static IUserContext UserWith(string permission)
    {
        var user = new Mock<IUserContext>();
        user.SetupGet(value => value.UserId).Returns(7);
        user.Setup(value => value.HasPermission(permission)).Returns(true);
        return user.Object;
    }
}