using Application.Features.Administration.ModerateImageTagChange;
using Application.Features.Images.ReplaceImageTags;
using Contracts;
using Entities.Models;
using Moq;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace GallerySiteUnitTests;

public sealed class ImageTagChangeHandlerTests
{
    [Fact]
    public async Task ReplaceTags_StaffEdit_AppliesTagsAndCreatesApprovedHistory()
    {
        var image = Image(Tag("old"));
        var replacement = new List<ImageTag> { Tag("new") };
        var (repositories, images, changes, transaction) = Repositories(image);
        images.Setup(repository => repository.GetOrCreateTagsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(replacement);
        ImageTagChange? added = null;
        changes.Setup(repository => repository.AddAsync(It.IsAny<ImageTagChange>(), It.IsAny<CancellationToken>()))
            .Callback<ImageTagChange, CancellationToken>((change, _) => added = change).Returns(Task.CompletedTask);
        var handler = new Application.Features.Images.ReplaceImageTags.Handler(repositories.Object, new TestUser(9, AppUserRole.Moderator), TimeProvider.System);

        var result = await handler.Handle(new Application.Features.Images.ReplaceImageTags.Command(image.Id, new ReplaceImageTagsDto(["new"])), CancellationToken.None);

        Assert.Equal(ImageTagChangeStatus.Approved, result.Status);
        Assert.Equal(["new"], image.Tags.Select(tag => tag.Name));
        Assert.NotNull(added);
        Assert.Equal(["old"], added.PreviousTags);
        Assert.Equal(["new"], added.ProposedTags);
        Assert.Equal(9, added.ReviewedByUserId);
        transaction.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ModerateTagChange_Approve_AppliesProposalAndRecordsReviewerNote()
    {
        var image = Image(Tag("old"));
        var change = PendingChange(image.Id, ["old"], ["new"]);
        var replacement = new List<ImageTag> { Tag("new") };
        var (repositories, images, changes, transaction) = Repositories(image);
        changes.Setup(repository => repository.GetByIdAsync(change.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync(change);
        images.Setup(repository => repository.GetOrCreateTagsAsync(change.ProposedTags, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(replacement);
        var handler = new Application.Features.Administration.ModerateImageTagChange.Handler(repositories.Object, new TestUser(9, AppUserRole.Admin), TimeProvider.System);

        var result = await handler.Handle(new Application.Features.Administration.ModerateImageTagChange.Command(change.Id, TagChangeDecision.Approve, " valid "), CancellationToken.None);

        Assert.Equal(ImageTagChangeStatus.Approved, result.Status);
        Assert.Equal(["new"], image.Tags.Select(tag => tag.Name));
        Assert.Equal("valid", change.ReviewNote);
        Assert.Equal(9, change.ReviewedByUserId);
        transaction.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ModerateTagChange_RejectPending_LeavesActiveTagsUntouched()
    {
        var image = Image(Tag("old"));
        var change = PendingChange(image.Id, ["old"], ["bad"]);
        var (repositories, _, changes, transaction) = Repositories(image);
        changes.Setup(repository => repository.GetByIdAsync(change.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync(change);
        var handler = new Application.Features.Administration.ModerateImageTagChange.Handler(repositories.Object, new TestUser(9, AppUserRole.Moderator), TimeProvider.System);

        var result = await handler.Handle(new Application.Features.Administration.ModerateImageTagChange.Command(change.Id, TagChangeDecision.Reject, "not suitable"), CancellationToken.None);

        Assert.Equal(ImageTagChangeStatus.Rejected, result.Status);
        Assert.Equal(["old"], image.Tags.Select(tag => tag.Name));
        Assert.Equal("not suitable", change.ReviewNote);
        transaction.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ModerateTagChange_RejectLatestApproved_RestoresPreviousTags()
    {
        var image = Image(Tag("new"));
        var change = PendingChange(image.Id, ["old"], ["new"]);
        change.Status = ImageTagChangeStatus.Approved;
        change.AppliedAtUtc = DateTimeOffset.UtcNow;
        var previous = new List<ImageTag> { Tag("old") };
        var (repositories, images, changes, transaction) = Repositories(image);
        changes.Setup(repository => repository.GetByIdAsync(change.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync(change);
        changes.Setup(repository => repository.GetLatestApprovedForImageAsync(image.Id, It.IsAny<CancellationToken>())).ReturnsAsync(change);
        images.Setup(repository => repository.GetOrCreateTagsAsync(change.PreviousTags, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(previous);
        var handler = new Application.Features.Administration.ModerateImageTagChange.Handler(repositories.Object, new TestUser(9, AppUserRole.Admin), TimeProvider.System);

        var result = await handler.Handle(new Application.Features.Administration.ModerateImageTagChange.Command(change.Id, TagChangeDecision.Reject, "undo"), CancellationToken.None);

        Assert.Equal(ImageTagChangeStatus.Reverted, result.Status);
        Assert.Equal(["old"], image.Tags.Select(tag => tag.Name));
        Assert.Equal(9, change.RevertedByUserId);
        Assert.Equal("undo", change.ReversionNote);
        transaction.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (Mock<IRepositoryManager> Repositories, Mock<IAppImageRepository> Images, Mock<IImageTagChangeRepository> Changes, Mock<IRepositoryTransaction> Transaction) Repositories(AppImage image)
    {
        var repositories = new Mock<IRepositoryManager>();
        var images = new Mock<IAppImageRepository>();
        var changes = new Mock<IImageTagChangeRepository>();
        var transaction = new Mock<IRepositoryTransaction>();
        repositories.SetupGet(repository => repository.AppImage).Returns(images.Object);
        repositories.SetupGet(repository => repository.ImageTagChanges).Returns(changes.Object);
        images.Setup(repository => repository.GetWithTagsByIdAsync(image.Id, It.IsAny<CancellationToken>())).ReturnsAsync(image);
        changes.Setup(repository => repository.GetPendingForImageAsync(image.Id, It.IsAny<CancellationToken>())).ReturnsAsync((ImageTagChange?)null);
        repositories.Setup(repository => repository.SaveAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repositories.Setup(repository => repository.BeginSerializableTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transaction.Object);
        transaction.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        transaction.Setup(value => value.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return (repositories, images, changes, transaction);
    }

    private static ImageTagChange PendingChange(int imageId, IReadOnlyList<string> previous, IReadOnlyList<string> proposed) => new()
    {
        Id = 8,
        ImageId = imageId,
        PreviousTags = previous.ToList(),
        ProposedTags = proposed.ToList(),
        Kind = ImageTagChangeKind.Replacement,
        Status = ImageTagChangeStatus.Pending,
        EditedByUserId = 7,
        EditedByLogin = "owner",
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    private static AppImage Image(params ImageTag[] tags) => new UserMadeImage
    {
        Id = 1,
        Source = ImageSource.UserUpload,
        UploadedById = 7,
        UploadedAtUtc = DateTimeOffset.UtcNow,
        Visibility = ImageVisibility.Gallery,
        ModerationStatus = ModerationStatus.Approved,
        StorageKey = "uploads/test.jpg",
        ContentType = "image/jpeg",
        Width = 1,
        Height = 1,
        Tags = tags.ToList(),
        EmbeddingStatus = EmbeddingStatus.Pending
    };

    private static ImageTag Tag(string name) => new()
    {
        Id = name == "old" ? 1 : 2,
        Name = name,
        NormalizedName = name,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        ModerationStatus = TagModerationStatus.Approved
    };

    private sealed class TestUser(int userId, params AppUserRole[] roles) : IUserContext
    {
        public int? UserId => userId;
        public string? Login => "tester";
        public bool IsInRole(string role) => roles.Any(candidate => candidate.ToString() == role);
        public void RequireAuthenticated() { }
    }
}
