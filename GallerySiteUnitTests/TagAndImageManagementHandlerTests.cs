using Application.Features.Administration.ChangeTagModeration;
using Application.Features.Images.HideImage;
using Application.Features.Images.ReplaceImageTags;
using Application.Features.Images.RestoreImage;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using Moq;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace GallerySiteUnitTests;

public sealed class TagAndImageManagementHandlerTests
{
    [Fact]
    public async Task ReplaceTags_OwnerEdit_CreatesPendingChangeWithoutChangingImage()
    {
        var image = Image(ownerId: 7, ModerationStatus.Approved);
        var tags = new List<ImageTag> { Tag("new-tag", TagModerationStatus.Pending) };
        var (repositories, images) = Repositories(image);
        images.Setup(repository => repository.GetOrCreateTagsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tags);
        var handler = new Application.Features.Images.ReplaceImageTags.Handler(repositories.Object, new TestUser(7), TimeProvider.System);

        var result = await handler.Handle(new Application.Features.Images.ReplaceImageTags.Command(1, new ReplaceImageTagsDto(["new-tag"])), CancellationToken.None);

        Assert.Equal(ModerationStatus.Approved, image.ModerationStatus);
        Assert.Empty(image.Tags);
        Assert.Equal(ImageTagChangeStatus.Pending, result.Status);
        repositories.Verify(repository => repository.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReplaceTags_StaffEdit_PreservesImageModeration()
    {
        var image = Image(ownerId: 7, ModerationStatus.Approved);
        var tags = new List<ImageTag> { Tag("approved", TagModerationStatus.Approved) };
        var (repositories, images) = Repositories(image);
        images.Setup(repository => repository.GetOrCreateTagsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tags);
        var handler = new Application.Features.Images.ReplaceImageTags.Handler(repositories.Object, new TestUser(9, AppUserRole.Moderator), TimeProvider.System);

        await handler.Handle(new Application.Features.Images.ReplaceImageTags.Command(1, new ReplaceImageTagsDto(["approved"])), CancellationToken.None);

        Assert.Equal(ModerationStatus.Approved, image.ModerationStatus);
    }

    [Fact]
    public async Task ReplaceTags_RejectedTag_ThrowsBadRequestAndDoesNotSave()
    {
        var image = Image(ownerId: 7, ModerationStatus.Approved);
        var (repositories, images) = Repositories(image);
        images.Setup(repository => repository.GetOrCreateTagsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Tag("blocked", TagModerationStatus.Rejected)]);
        var handler = new Application.Features.Images.ReplaceImageTags.Handler(repositories.Object, new TestUser(7), TimeProvider.System);

        await Assert.ThrowsAsync<Base400BadRequestException>(() => handler.Handle(
            new Application.Features.Images.ReplaceImageTags.Command(1, new ReplaceImageTagsDto(["blocked"])), CancellationToken.None));

        repositories.Verify(repository => repository.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangeTagModeration_Rejected_RemovesAllImageLinks()
    {
        var tag = Tag("blocked", TagModerationStatus.Pending);
        var image = Image(ownerId: 7, ModerationStatus.Approved);
        image.Tags.Add(tag);
        tag.AppImages.Add(image);
        var repositories = new Mock<IRepositoryManager>();
        var transaction = ConfigureTransaction(repositories);
        var images = new Mock<IAppImageRepository>();
        var tagChanges = new Mock<IImageTagChangeRepository>();
        repositories.SetupGet(repository => repository.AppImage).Returns(images.Object);
        repositories.SetupGet(repository => repository.ImageTagChanges).Returns(tagChanges.Object);
        images.Setup(repository => repository.GetTagByIdAsync(tag.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync(tag);
        tagChanges.Setup(repository => repository.GetPendingContainingTagAsync(tag.NormalizedName, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        List<ImageTagChange>? recorded = null;
        tagChanges.Setup(repository => repository.AddRangeAsync(It.IsAny<IEnumerable<ImageTagChange>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ImageTagChange>, CancellationToken>((changes, _) => recorded = changes.ToList()).Returns(Task.CompletedTask);
        var handler = new Application.Features.Administration.ChangeTagModeration.Handler(repositories.Object, new TestUser(9, AppUserRole.Admin), TimeProvider.System);

        var result = await handler.Handle(new Application.Features.Administration.ChangeTagModeration.Command(tag.Id, TagModerationStatus.Rejected), CancellationToken.None);

        Assert.Equal(TagModerationStatus.Rejected, result.Status);
        Assert.Equal(0, result.LinkedImageCount);
        Assert.Empty(tag.AppImages);
        Assert.Empty(image.Tags);
        var audit = Assert.Single(recorded!);
        Assert.Equal(ImageTagChangeKind.GlobalTagRejection, audit.Kind);
        Assert.Equal(["blocked"], audit.PreviousTags);
        Assert.Empty(audit.ProposedTags);
        transaction.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangeTagModeration_Approved_PreservesImageLinks()
    {
        var tag = Tag("review", TagModerationStatus.Pending);
        tag.AppImages.Add(Image(ownerId: 7, ModerationStatus.Pending));
        var repositories = new Mock<IRepositoryManager>();
        ConfigureTransaction(repositories);
        var images = new Mock<IAppImageRepository>();
        repositories.SetupGet(repository => repository.AppImage).Returns(images.Object);
        images.Setup(repository => repository.GetTagByIdAsync(tag.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync(tag);
        var handler = new Application.Features.Administration.ChangeTagModeration.Handler(repositories.Object, new TestUser(9, AppUserRole.Moderator), TimeProvider.System);

        var result = await handler.Handle(new Application.Features.Administration.ChangeTagModeration.Command(tag.Id, TagModerationStatus.Approved), CancellationToken.None);

        Assert.Equal(TagModerationStatus.Approved, result.Status);
        Assert.Equal(1, result.LinkedImageCount);
        Assert.Single(tag.AppImages);
    }

    [Fact]
    public async Task ChangeTagModeration_Rejected_AutoRejectsPendingChangesContainingTag()
    {
        var tag = Tag("blocked", TagModerationStatus.Pending);
        var pending = new ImageTagChange
        {
            Id = 4,
            ImageId = 1,
            PreviousTags = ["safe"],
            ProposedTags = ["blocked"],
            Kind = ImageTagChangeKind.Replacement,
            Status = ImageTagChangeStatus.Pending,
            EditedByLogin = "owner",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var repositories = new Mock<IRepositoryManager>();
        ConfigureTransaction(repositories);
        var images = new Mock<IAppImageRepository>();
        var tagChanges = new Mock<IImageTagChangeRepository>();
        repositories.SetupGet(repository => repository.AppImage).Returns(images.Object);
        repositories.SetupGet(repository => repository.ImageTagChanges).Returns(tagChanges.Object);
        images.Setup(repository => repository.GetTagByIdAsync(tag.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync(tag);
        tagChanges.Setup(repository => repository.GetPendingContainingTagAsync(tag.NormalizedName, It.IsAny<CancellationToken>())).ReturnsAsync([pending]);
        tagChanges.Setup(repository => repository.AddRangeAsync(It.IsAny<IEnumerable<ImageTagChange>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var handler = new Application.Features.Administration.ChangeTagModeration.Handler(repositories.Object, new TestUser(9, AppUserRole.Admin), TimeProvider.System);

        await handler.Handle(new Application.Features.Administration.ChangeTagModeration.Command(tag.Id, TagModerationStatus.Rejected), CancellationToken.None);

        Assert.Equal(ImageTagChangeStatus.Rejected, pending.Status);
        Assert.Equal(9, pending.ReviewedByUserId);
        Assert.Contains("automatically", pending.ReviewNote);
    }

    [Fact]
    public async Task HideThenRestore_Owner_SetsTimestampAndPendingStatus()
    {
        var image = Image(ownerId: 7, ModerationStatus.Approved);
        var (repositories, _) = Repositories(image);
        var user = new TestUser(7);
        var hide = new Application.Features.Images.HideImage.Handler(repositories.Object, user, TimeProvider.System);
        var restore = new Application.Features.Images.RestoreImage.Handler(repositories.Object, user);

        await hide.Handle(new Application.Features.Images.HideImage.Command(1), CancellationToken.None);
        Assert.NotNull(image.DeletedAtUtc);

        await restore.Handle(new Application.Features.Images.RestoreImage.Command(1), CancellationToken.None);
        Assert.Null(image.DeletedAtUtc);
        Assert.Equal(ModerationStatus.Pending, image.ModerationStatus);
    }

    [Theory]
    [InlineData(ModerationStatus.Pending)]
    [InlineData(ModerationStatus.Approved)]
    [InlineData(ModerationStatus.Rejected)]
    public async Task Publish_PrivateImage_PersistsGalleryVisibilityAndPreservesModeration(ModerationStatus status)
    {
        var image = Image(ownerId: 7, status, ImageVisibility.Private);
        var (repositories, _) = Repositories(image);
        var handler = new Application.Features.Images.PublishImage.Handler(repositories.Object, new TestUser(7));

        var result = await handler.Handle(new Application.Features.Images.PublishImage.Command(1), CancellationToken.None);

        Assert.Equal(ImageVisibility.Gallery, image.Visibility);
        Assert.Equal(status, image.ModerationStatus);
        Assert.Equal(ImageVisibility.Gallery, result.Visibility);
        Assert.Equal(status, result.ModerationStatus);
        repositories.Verify(repository => repository.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_PrivateImage_AllowsStaffWhoDoesNotOwnIt()
    {
        var image = Image(ownerId: 7, ModerationStatus.Pending, ImageVisibility.Private);
        var (repositories, _) = Repositories(image);
        var handler = new Application.Features.Images.PublishImage.Handler(repositories.Object, new TestUser(9, AppUserRole.Moderator));

        await handler.Handle(new Application.Features.Images.PublishImage.Command(1), CancellationToken.None);

        Assert.Equal(ImageVisibility.Gallery, image.Visibility);
        repositories.Verify(repository => repository.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_OtherUser_ThrowsForbiddenAndDoesNotSave()
    {
        var image = Image(ownerId: 7, ModerationStatus.Approved, ImageVisibility.Private);
        var (repositories, _) = Repositories(image);
        var handler = new Application.Features.Images.PublishImage.Handler(repositories.Object, new TestUser(8));

        await Assert.ThrowsAsync<AppForbiddenException>(() => handler.Handle(new Application.Features.Images.PublishImage.Command(1), CancellationToken.None));

        Assert.Equal(ImageVisibility.Private, image.Visibility);
        repositories.Verify(repository => repository.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Publish_GalleryImage_ThrowsBadRequestAndDoesNotSave()
    {
        var image = Image(ownerId: 7, ModerationStatus.Approved);
        var (repositories, _) = Repositories(image);
        var handler = new Application.Features.Images.PublishImage.Handler(repositories.Object, new TestUser(7));

        await Assert.ThrowsAsync<Base400BadRequestException>(() => handler.Handle(new Application.Features.Images.PublishImage.Command(1), CancellationToken.None));

        Assert.Equal(ImageVisibility.Gallery, image.Visibility);
        repositories.Verify(repository => repository.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (Mock<IRepositoryManager> Repositories, Mock<IAppImageRepository> Images) Repositories(AppImage image)
    {
        var repositories = new Mock<IRepositoryManager>();
        var images = new Mock<IAppImageRepository>();
        var tagChanges = new Mock<IImageTagChangeRepository>();
        repositories.SetupGet(repository => repository.AppImage).Returns(images.Object);
        repositories.SetupGet(repository => repository.ImageTagChanges).Returns(tagChanges.Object);
        images.Setup(repository => repository.GetByIdAsync(image.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync(image);
        tagChanges.Setup(repository => repository.GetPendingForImageAsync(image.Id, It.IsAny<CancellationToken>())).ReturnsAsync((ImageTagChange?)null);
        tagChanges.Setup(repository => repository.AddAsync(It.IsAny<ImageTagChange>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repositories.Setup(repository => repository.SaveAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ConfigureTransaction(repositories);
        return (repositories, images);
    }

    private static Mock<IRepositoryTransaction> ConfigureTransaction(Mock<IRepositoryManager> repositories)
    {
        var transaction = new Mock<IRepositoryTransaction>();
        transaction.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        transaction.Setup(value => value.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repositories.Setup(repository => repository.BeginSerializableTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transaction.Object);
        return transaction;
    }

    private static AppImage Image(int ownerId, ModerationStatus status, ImageVisibility visibility = ImageVisibility.Gallery) => new UserMadeImage
    {
        Id = 1,
        Source = ImageSource.UserUpload,
        UploadedById = ownerId,
        UploadedAtUtc = DateTimeOffset.UtcNow,
        Visibility = visibility,
        ModerationStatus = status,
        StorageKey = "uploads/test.jpg",
        ContentType = "image/jpeg",
        Width = 1,
        Height = 1,
        EmbeddingStatus = EmbeddingStatus.Pending
    };

    private static ImageTag Tag(string name, TagModerationStatus status) => new()
    {
        Id = 2,
        Name = name,
        NormalizedName = name,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        ModerationStatus = status
    };

    private sealed class TestUser(int userId, params AppUserRole[] roles) : IUserContext
    {
        public int? UserId => userId;
        public string? Login => "tester";
        public bool IsInRole(string role) => roles.Any(candidate => string.Equals(candidate.ToString(), role, StringComparison.Ordinal));
        public void RequireAuthenticated() { }
    }
}
