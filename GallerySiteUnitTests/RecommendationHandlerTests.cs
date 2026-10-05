using Contracts;
using Entities.Exceptions;
using Entities.Models;
using Moq;
using Pgvector;
using Service.Contracts;

namespace GallerySiteUnitTests;

public sealed class RecommendationHandlerTests
{
    [Fact]
    public async Task GetRecommendations_ClampsPaginationAndMapsCards()
    {
        var source = Image(1, 7, embedded: true);
        var recommendation = Image(2, 8, embedded: true);
        recommendation.UploadedBy = new AppUser
        {
            Id = 8,
            Login = "artist",
            NormalizedLogin = "ARTIST",
            PasswordHash = "unused",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var (repositories, images) = Repositories(source);
        images.Setup(repository => repository.GetRecommendationsAsync(1, 1, 50, It.IsAny<ImageViewer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(([ImageReadTestData.Card(recommendation)], 51));
        var handler = new Application.Features.Images.GetImageRecommendation.Handler(repositories.Object, new TestUser(7));

        var result = await handler.Handle(new Application.Features.Images.GetImageRecommendation.Command(1, 0, 500), CancellationToken.None);

        Assert.Equal(1, result.Page);
        Assert.Equal(50, result.PageSize);
        Assert.Equal(51, result.Total);
        var image = Assert.Single(result.Images);
        Assert.Equal(2, image.Id);
        Assert.Equal("artist", image.UploadedBy);
    }

    [Fact]
    public async Task GetRecommendations_MissingImage_ThrowsNotFound()
    {
        var repositories = new Mock<IRepositoryManager>();
        var images = new Mock<IAppImageRepository>();
        repositories.SetupGet(repository => repository.AppImage).Returns(images.Object);
        images.Setup(repository => repository.GetMetadataByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((ImageMetadata?)null);
        var handler = new Application.Features.Images.GetImageRecommendation.Handler(repositories.Object, new TestUser(7));

        await Assert.ThrowsAsync<Base404ReturnException>(() => handler.Handle(
            new Application.Features.Images.GetImageRecommendation.Command(1), CancellationToken.None));
    }

    [Fact]
    public async Task GetRecommendations_WithoutEmbedding_ThrowsConflict()
    {
        var source = Image(1, 7, embedded: false);
        var (repositories, _) = Repositories(source);
        var handler = new Application.Features.Images.GetImageRecommendation.Handler(repositories.Object, new TestUser(7));

        await Assert.ThrowsAsync<Base409ConflictException>(() => handler.Handle(
            new Application.Features.Images.GetImageRecommendation.Command(1), CancellationToken.None));
    }

    [Fact]
    public async Task GetRecommendations_UnreadableImage_ThrowsForbidden()
    {
        var source = Image(1, 7, embedded: true, visibility: ImageVisibility.Private);
        var (repositories, _) = Repositories(source);
        var handler = new Application.Features.Images.GetImageRecommendation.Handler(repositories.Object, new TestUser(8));

        await Assert.ThrowsAsync<AppForbiddenException>(() => handler.Handle(
            new Application.Features.Images.GetImageRecommendation.Command(1), CancellationToken.None));
    }

    private static (Mock<IRepositoryManager> Repositories, Mock<IAppImageRepository> Images) Repositories(AppImage image)
    {
        var repositories = new Mock<IRepositoryManager>();
        var images = new Mock<IAppImageRepository>();
        repositories.SetupGet(repository => repository.AppImage).Returns(images.Object);
        images.Setup(repository => repository.GetMetadataByIdAsync(image.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => ImageReadTestData.Metadata(image));
        return (repositories, images);
    }

    private static AppImage Image(int id, int ownerId, bool embedded, ImageVisibility visibility = ImageVisibility.Gallery) => new UserMadeImage
    {
        Id = id,
        Source = ImageSource.UserUpload,
        UploadedById = ownerId,
        UploadedAtUtc = DateTimeOffset.UtcNow,
        Visibility = visibility,
        ModerationStatus = ModerationStatus.Approved,
        StorageKey = $"uploads/{id}.jpg",
        ContentType = "image/jpeg",
        Width = 1,
        Height = 1,
        Embedding = embedded ? new Vector(new float[] { 1f }) : null,
        EmbeddingStatus = embedded ? EmbeddingStatus.Ready : EmbeddingStatus.Pending
    };

    private sealed class TestUser(int userId) : IUserContext
    {
        public int? UserId => userId;
        public string? Login => "tester";
        public bool IsInRole(string role) => false;
        public void RequireAuthenticated() { }
    }
}
