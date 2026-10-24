using System.Text.Json;
using Application.Features.Images.GetImageBySearch;
using Contracts;
using Entities.Exceptions;
using Moq;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace GallerySiteUnitTests;

public sealed class RandomImageSearchTests
{
    [Fact]
    public async Task Handle_RandomFirstPageWithoutSeed_GeneratesAndReturnsSeed()
    {
        var (handler, images) = CreateHandler();
        SearchImageDto? captured = null;
        images.Setup(repository => repository.SearchAsync(It.IsAny<SearchImageDto>(), It.IsAny<ImageViewer>(), It.IsAny<CancellationToken>()))
            .Callback<SearchImageDto, ImageViewer, CancellationToken>((request, _, _) => captured = request)
            .ReturnsAsync(([], 3));

        var result = await handler.Handle(new Command(new SearchImageDto(null, Sort: ImageSort.Random)), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(ImageSort.Random, captured!.Sort);
        Assert.True(RandomSortSeed.TryGetValue(captured.RandomSeed, out _));
        Assert.Equal(captured.RandomSeed, result.RandomSeed);
        Assert.Equal(3, result.Total);
    }

    [Fact]
    public async Task Handle_RandomLaterPageWithoutSeed_ThrowsBadRequest()
    {
        var (handler, _) = CreateHandler();

        var exception = await Assert.ThrowsAsync<Base400BadRequestException>(() => handler.Handle(
            new Command(new SearchImageDto(null, Sort: ImageSort.Random, Page: 2)), CancellationToken.None));

        Assert.Contains("randomSeed is required", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-valid-seed")]
    public async Task Handle_RandomWithMalformedSeed_ThrowsBadRequest(string randomSeed)
    {
        var (handler, _) = CreateHandler();

        var exception = await Assert.ThrowsAsync<Base400BadRequestException>(() => handler.Handle(
            new Command(new SearchImageDto(null, Sort: ImageSort.Random, RandomSeed: randomSeed)), CancellationToken.None));

        Assert.Contains("randomSeed must be", exception.Message);
    }

    [Fact]
    public async Task Handle_RandomWithSuppliedSeed_ForwardsAndReturnsSameSeed()
    {
        const string randomSeed = "AAAAAAAAAAA";
        var (handler, images) = CreateHandler();
        images.Setup(repository => repository.SearchAsync(It.Is<SearchImageDto>(request => request.RandomSeed == randomSeed), It.IsAny<ImageViewer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(([], 0));

        var result = await handler.Handle(new Command(new SearchImageDto(null, Sort: ImageSort.Random, RandomSeed: randomSeed)), CancellationToken.None);

        Assert.Equal(randomSeed, result.RandomSeed);
    }

    [Fact]
    public async Task Handle_NonRandomSort_IgnoresRandomSeed()
    {
        var (handler, images) = CreateHandler();
        images.Setup(repository => repository.SearchAsync(It.Is<SearchImageDto>(request => request.Sort == ImageSort.Newest && request.RandomSeed == null),
                It.IsAny<ImageViewer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(([], 0));

        var result = await handler.Handle(new Command(new SearchImageDto(null, RandomSeed: "not-a-valid-seed")), CancellationToken.None);

        Assert.Null(result.RandomSeed);
    }

    [Fact]
    public void PageableImagesDto_NonRandomResponseOmitsRandomSeed()
    {
        var response = new PageableImagesDto(1, 20, 0, ImageSort.Newest, []);

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("randomSeed", json, StringComparison.Ordinal);
    }

    [Fact]
    public void PageableImagesDto_RandomResponseIncludesRandomSeed()
    {
        var response = new PageableImagesDto(1, 20, 0, ImageSort.Random, [], "AAAAAAAAAAA");

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"randomSeed\":\"AAAAAAAAAAA\"", json, StringComparison.Ordinal);
    }

    private static (Handler Handler, Mock<IAppImageRepository> Images) CreateHandler()
    {
        var repositories = new Mock<IRepositoryManager>();
        var images = new Mock<IAppImageRepository>();
        repositories.SetupGet(repository => repository.AppImage).Returns(images.Object);
        return (new Handler(repositories.Object, new TestUser()), images);
    }

    private sealed class TestUser : IUserContext
    {
        public int? UserId => 7;
        public string? Login => "tester";
        public bool IsInRole(string role) => false;
        public void RequireAuthenticated() { }
    }
}
