using Contracts;
using Entities.Models;
using Moq;
using Service.Contracts;

namespace GallerySiteUnitTests;

public sealed class LikedImagesHandlerTests
{
    [Fact]
    public async Task GetLikedImages_ClampsPaginationAndMapsCards()
    {
        var likedImage = new UserMadeImage
        {
            Id = 42,
            Source = ImageSource.UserUpload,
            UploadedById = 7,
            UploadedBy = new AppUser
            {
                Id = 8,
                Login = "artist",
                NormalizedLogin = "ARTIST",
                PasswordHash = "unused",
                CreatedAtUtc = DateTimeOffset.UtcNow
            },
            UploadedAtUtc = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero),
            Visibility = ImageVisibility.Gallery,
            ModerationStatus = ModerationStatus.Approved,
            StorageKey = "uploads/42.jpg",
            ContentType = "image/jpeg",
            Width = 1920,
            Height = 1080,
            EmbeddingStatus = EmbeddingStatus.Ready,
            Tags = [Tag("portrait", TagModerationStatus.Approved), Tag("review", TagModerationStatus.Pending)],
            Likes = [new ImageLike { ImageId = 42, UserId = 7, CreatedAtUtc = DateTimeOffset.UtcNow }, new ImageLike { ImageId = 42, UserId = 9, CreatedAtUtc = DateTimeOffset.UtcNow }],
            Comments = [new Comment { Id = 1, ImageId = 42, AuthorId = 8, Content = "Visible", CreatedAtUtc = DateTimeOffset.UtcNow },
                new Comment { Id = 2, ImageId = 42, AuthorId = 8, Content = "Hidden", CreatedAtUtc = DateTimeOffset.UtcNow, DeletedAtUtc = DateTimeOffset.UtcNow }]
        };
        var repositories = new Mock<IRepositoryManager>();
        var images = new Mock<IAppImageRepository>();
        repositories.SetupGet(repository => repository.AppImage).Returns(images.Object);
        images.Setup(repository => repository.GetLikedByUserAsync(7, 1, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([likedImage], 3));
        var handler = new Application.Features.Images.GetLikedImages.Handler(repositories.Object, new TestUser(7));

        var result = await handler.Handle(new Application.Features.Images.GetLikedImages.Command(0, 500), CancellationToken.None);

        Assert.Equal(1, result.Page);
        Assert.Equal(50, result.PageSize);
        Assert.Equal(3, result.Total);
        var image = Assert.Single(result.Images);
        Assert.Equal(42, image.Id);
        Assert.Equal("artist", image.UploadedBy);
        Assert.Equal(["portrait"], image.Tags);
        Assert.Equal(["review"], image.PendingTags);
        Assert.Equal(2, image.LikeCount);
        Assert.Equal(1, image.CommentCount);
        Assert.True(image.IsLikedByCurrentUser);
    }

    private static ImageTag Tag(string name, TagModerationStatus status) => new()
    {
        Name = name,
        NormalizedName = name,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        ModerationStatus = status
    };

    private sealed class TestUser(int userId) : IUserContext
    {
        public int? UserId => userId;
        public string? Login => "tester";
        public bool IsInRole(string role) => false;
        public void RequireAuthenticated() { }
    }
}
