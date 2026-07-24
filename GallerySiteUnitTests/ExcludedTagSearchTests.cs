using Application.Features.Images.GetImageBySearch;
using Entities.Models;
using GallerySiteBackend.Presentation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Pgvector.EntityFrameworkCore;
using Repository;
using Shared.DataTransferObjects;

namespace GallerySiteUnitTests;

public sealed class ExcludedTagSearchTests
{
    [Fact]
    public async Task Search_RepeatedExcludedTags_ForwardsValuesToSearchDto()
    {
        var mediator = new Mock<IMediator>();
        Command? captured = null;
        mediator.Setup(service => service.Send(It.IsAny<Command>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<PageableImagesDto>, CancellationToken>((command, _) => captured = (Command)command)
            .ReturnsAsync(new PageableImagesDto(1, 20, 0, ImageSort.Newest, []));
        var controller = new ImageController(mediator.Object);

        await controller.Search(null, excludedTags: [" summer ", "winter"]);

        Assert.Equal([" summer ", "winter"], captured!.Request.ExcludedTags);
    }

    [Fact]
    public void ApplyTagFilters_ExclusionOnly_ExcludesAnyApprovedMatch()
    {
        var images = new[]
        {
            Image(1, Tag("summer"), Tag("portrait")),
            Image(2, Tag("winter")),
            Image(3, Tag("portrait"))
        }.AsQueryable();

        var result = AppImageRepository.ApplyTagFilters(images, null, ["summer", "winter"]).Select(image => image.Id);

        Assert.Equal([3], result);
    }

    [Fact]
    public void ApplyTagFilters_PositiveAndExcluded_AppliesBothPredicates()
    {
        var images = new[]
        {
            Image(1, Tag("portrait"), Tag("summer")),
            Image(2, Tag("portrait")),
            Image(3, Tag("summer"))
        }.AsQueryable();

        var result = AppImageRepository.ApplyTagFilters(images, ["portrait"], ["summer"]).Select(image => image.Id);

        Assert.Equal([2], result);
    }

    [Fact]
    public void ApplyTagFilters_NormalizesDeduplicatesAndIgnoresEmptyValues()
    {
        var images = new[]
        {
            Image(1, Tag("portrait")),
            Image(2, Tag("summer")),
            Image(3, Tag("winter"))
        }.AsQueryable();

        var result = AppImageRepository.ApplyTagFilters(images,
            [" PORTRAIT ", "portrait", " "], [" SUMMER ", "summer", ""]).Select(image => image.Id);

        Assert.Equal([1], result);
    }

    [Fact]
    public void ApplyTagFilters_EmptyInputs_DoNotFilter()
    {
        var images = new[] { Image(1), Image(2) }.AsQueryable();

        var result = AppImageRepository.ApplyTagFilters(images, [" ", ""], []).Select(image => image.Id);

        Assert.Equal([1, 2], result);
    }

    [Fact]
    public void ApplyTagFilters_NormalizesEachListWithIndependentTwentyItemCap()
    {
        var positiveTags = Enumerable.Range(1, 20).Select(index => Tag($"positive-{index}")).ToArray();
        var included = Image(1, positiveTags);
        var excludedAfterCap = Image(2, positiveTags.Append(Tag("excluded-21")).ToArray());
        var excludedWithinCap = Image(3, positiveTags.Append(Tag("excluded-1")).ToArray());
        var requestedPositive = Enumerable.Range(1, 21).Select(index => $"positive-{index}").ToArray();
        var requestedExcluded = Enumerable.Range(1, 21).Select(index => $"excluded-{index}").ToArray();

        var result = AppImageRepository.ApplyTagFilters(
            new[] { included, excludedAfterCap, excludedWithinCap }.AsQueryable(),
            requestedPositive, requestedExcluded).Select(image => image.Id);

        Assert.Equal([1, 2], result);
    }

    [Fact]
    public void ApplyTagFilters_PendingExcludedTag_DoesNotExclude()
    {
        var images = new[] { Image(1, Tag("summer", TagModerationStatus.Pending)) }.AsQueryable();

        var result = AppImageRepository.ApplyTagFilters(images, null, ["summer"]).Select(image => image.Id);

        Assert.Equal([1], result);
    }

    [Fact]
    public void ApplyTagFilters_SameTagInBothLists_ReturnsNoMatches()
    {
        var images = new[] { Image(1, Tag("portrait")), Image(2) }.AsQueryable();

        var result = AppImageRepository.ApplyTagFilters(images, ["portrait"], [" PORTRAIT "]);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(ImageSort.Newest)]
    [InlineData(ImageSort.Oldest)]
    [InlineData(ImageSort.MediaId)]
    [InlineData(ImageSort.Random)]
    public void ApplyTagFilters_AllSorts_TranslateWithPostgresProvider(ImageSort sort)
    {
        var options = new DbContextOptionsBuilder<RepositoryContext>()
            .UseNpgsql("Host=localhost;Database=gallery;Username=gallery;Password=gallery",
                postgres => postgres.UseVector())
            .Options;
        using var context = new RepositoryContext(options);

        var query = AppImageRepository.ApplyTagFilters(context.Images, ["portrait"], ["summer", "winter"]);
        var sql = AppImageRepository.ApplySort(query, sort, 42).ToQueryString();

        Assert.Contains("NOT EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Approved", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static UserMadeImage Image(int id, params ImageTag[] tags) => new()
    {
        Id = id,
        StorageKey = $"{id}.jpg",
        ContentType = "image/jpeg",
        Tags = [.. tags]
    };

    private static ImageTag Tag(string name,
        TagModerationStatus status = TagModerationStatus.Approved) => new()
    {
        Name = name,
        NormalizedName = name,
        ModerationStatus = status
    };
}
