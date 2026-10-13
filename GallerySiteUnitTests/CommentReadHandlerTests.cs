using Application.Features.Images.Comments;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using Moq;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace GallerySiteUnitTests;

public sealed class CommentReadHandlerTests
{
    [Fact]
    public async Task GetComments_PublicImage_ReturnsProjectedValuesAndClampsPaging()
    {
        var repository = new Mock<IRepositoryManager>();
        var images = new Mock<IAppImageRepository>();
        var interactions = new Mock<IInteractionRepository>();
        repository.SetupGet(item => item.AppImage).Returns(images.Object);
        repository.SetupGet(item => item.Interactions).Returns(interactions.Object);
        images.Setup(item => item.GetMetadataByIdAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Metadata());
        var comments = new List<CommentDto>
        {
            new(3, 7, 12, "alice", "  preserved content  ", DateTimeOffset.UnixEpoch),
            new(4, 7, 13, string.Empty, "fallback login", DateTimeOffset.UnixEpoch)
        };
        using var cancellation = new CancellationTokenSource();
        interactions.Setup(item => item.GetCommentsAsync(7, 1, 50, cancellation.Token)).ReturnsAsync((comments, 2));
        var handler = new GetCommentsHandler(repository.Object, new User());

        var result = await handler.Handle(new(7, int.MinValue, int.MaxValue), cancellation.Token);

        Assert.Equal(1, result.Page);
        Assert.Equal(50, result.PageSize);
        Assert.Equal(2, result.Total);
        Assert.Equal(comments, result.Comments);
    }

    [Theory]
    [InlineData(ImageVisibility.Private, ModerationStatus.Approved, false)]
    [InlineData(ImageVisibility.Gallery, ModerationStatus.Pending, false)]
    [InlineData(ImageVisibility.Gallery, ModerationStatus.Rejected, false)]
    [InlineData(ImageVisibility.Gallery, ModerationStatus.Approved, true)]
    public async Task GetComments_NonPublicImage_RejectsBeforeReadingComments(
        ImageVisibility visibility, ModerationStatus moderation, bool deleted)
    {
        var repository = new Mock<IRepositoryManager>();
        var images = new Mock<IAppImageRepository>();
        repository.SetupGet(item => item.AppImage).Returns(images.Object);
        images.Setup(item => item.GetMetadataByIdAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Metadata() with { Visibility = visibility, ModerationStatus = moderation,
                DeletedAtUtc = deleted ? DateTimeOffset.UnixEpoch : null });
        var handler = new GetCommentsHandler(repository.Object, new User());

        await Assert.ThrowsAsync<Base404ReturnException>(() => handler.Handle(new(7, 1, 50), CancellationToken.None));

        repository.VerifyGet(item => item.Interactions, Times.Never);
    }

    [Fact]
    public async Task GetComments_MissingImage_ReturnsNotFound()
    {
        var repository = new Mock<IRepositoryManager>();
        repository.SetupGet(item => item.AppImage).Returns(new Mock<IAppImageRepository>().Object);
        var handler = new GetCommentsHandler(repository.Object, new User());
        await Assert.ThrowsAsync<Base404ReturnException>(() => handler.Handle(new(7, 1, 50), CancellationToken.None));
        repository.VerifyGet(item => item.Interactions, Times.Never);
    }

    [Fact]
    public async Task GetComments_Unauthenticated_RejectsBeforeRepositoryAccess()
    {
        var repository = new Mock<IRepositoryManager>(MockBehavior.Strict);
        var handler = new GetCommentsHandler(repository.Object, new User(false));
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() => handler.Handle(new(7, 1, 50), CancellationToken.None));
        repository.VerifyNoOtherCalls();
    }

    private static ImageMetadata Metadata() => new(7, 12, ImageVisibility.Gallery,
        ModerationStatus.Approved, null, "image.jpg", "image/jpeg", false);

    private sealed class User(bool authenticated = true) : IUserContext
    {
        public int? UserId => authenticated ? 12 : null;
        public string? Login => authenticated ? "alice" : null;
        public bool IsInRole(string role) => false;
        public void RequireAuthenticated()
        {
            if (!authenticated) throw new AppUserUnauthorizedException("Authentication is required.");
        }
    }
}
