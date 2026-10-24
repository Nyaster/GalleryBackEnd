using AngleSharp.Html.Parser;
using Contracts;
using Entities.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Service;
using Service.Contracts;
using System.Collections;
using System.Reflection;

namespace GallerySiteUnitTests;

public sealed class AppImageParserServiceTests
{
    [Fact]
    public void ExtractCandidates_UsesImageSourceAndRemovesOnlyTransformParameters()
    {
        const string html = """
            <div class="requests"><div class="block"><div class="block-inner">
              <a href="/wrong-link.jpg?width=1"><img src="/images/source.jpg?width=320&height=180&format=webp&quality=60&v=42&token=keep" srcset="/images/srcset.jpg 2x" /></a>
              <div class="like" data-media-id="123"></div>
              <div class="overlay"><p>Added: 7/13/2026</p><p class="tags">tags: first, second</p></div>
            </div></div></div>
            """;

        var candidate = Assert.Single(ExtractCandidates(html));
        var imageUrl = (Uri)candidate.GetType().GetProperty("ImageUrl")!.GetValue(candidate)!;

        Assert.Equal("https://source.example/images/source.jpg?v=42&token=keep", imageUrl.AbsoluteUri);
    }

    [Fact]
    public void ExtractCandidates_GifImage_IsIgnored()
    {
        const string html = """
            <div class="requests"><div class="block"><div class="block-inner">
              <img src="/images/animated.gif" />
              <div class="like" data-media-id="123"></div>
              <div class="overlay"><p>Added: 7/13/2026</p></div>
            </div></div></div>
            """;

        Assert.Empty(ExtractCandidates(html));
    }

    [Fact]
    public void EnsureAllowedImageUri_UnexpectedHost_IsRejected()
    {
        var method = typeof(AppImageParserService).GetMethod("EnsureAllowedImageUri", BindingFlags.Static | BindingFlags.NonPublic)!;
        var settings = new ParserSettings { SiteUrl = "https://source.example", AllowedImageHosts = ["source.example"] };

        var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [new Uri("https://untrusted.example/image.jpg"), settings]));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersistCandidateAsync_PersistenceCompletes_OnlyRetainsSuccessfullySavedFile(bool saved)
    {
        using var files = new ImageStagingTestFiles();
        AppImage? addedImage = null;
        var tags = new List<ImageTag> { new() { Name = "source tag", NormalizedName = "source tag" } };
        var images = new Mock<IAppImageRepository>();
        images.Setup(repository => repository.GetOrCreateTagsAsync(It.IsAny<IEnumerable<string>>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(tags);
        images.Setup(repository => repository.AddAsync(It.IsAny<AppImage>(), It.IsAny<CancellationToken>()))
            .Callback((AppImage image, CancellationToken _) => addedImage = image)
            .Returns(Task.CompletedTask);
        var repositories = new Mock<IRepositoryManager>();
        repositories.SetupGet(repository => repository.AppImage).Returns(images.Object);
        repositories.Setup(repository => repository.SaveAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Assert.NotNull(addedImage);
                Assert.True(files.Storage.Exists(addedImage.StorageKey));
                return saved ? Task.CompletedTask : Task.FromException(new IOException("Save failed."));
            });
        var parser = new AppImageParserService(repositories.Object, files.Stager,
            Options.Create(new ParserSettings()), TimeProvider.System, NullLogger<AppImageParserService>.Instance);
        var candidateType = typeof(AppImageParserService).GetNestedType("ScrapedCandidate", BindingFlags.NonPublic)!;
        var uploadedAt = new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero);
        var candidate = Activator.CreateInstance(candidateType,
            [123, new Uri("https://source.example/image.png"), uploadedAt, new[] { "source tag" }])!;
        var persist = typeof(AppImageParserService).GetMethod("PersistCandidateAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await using var source = new MemoryStream([1, 2, 3]);
        var operation = (Task)persist.Invoke(parser, [candidate, source, CancellationToken.None])!;

        if (saved) await operation;
        else await Assert.ThrowsAsync<IOException>(() => operation);

        Assert.NotNull(addedImage);
        Assert.IsType<SelebusImage>(addedImage);
        Assert.Equal(ImageSource.Scraped, addedImage.Source);
        Assert.Equal(123, addedImage.ExternalMediaId);
        Assert.Equal(uploadedAt, addedImage.UploadedAtUtc);
        Assert.Equal(ImageVisibility.Gallery, addedImage.Visibility);
        Assert.Equal(ModerationStatus.Approved, addedImage.ModerationStatus);
        Assert.Equal(AiUsageClassification.HumanMade, addedImage.AiUsage);
        Assert.Equal(EmbeddingStatus.Pending, addedImage.EmbeddingStatus);
        Assert.Same(tags, addedImage.Tags);
        Assert.Matches(@"^scraped/123-[a-f0-9]{32}\.png$", addedImage.StorageKey);
        Assert.Equal(ImageStagingTestFiles.Metadata.ContentType, addedImage.ContentType);
        Assert.Equal(ImageStagingTestFiles.Metadata.Width, addedImage.Width);
        Assert.Equal(ImageStagingTestFiles.Metadata.Height, addedImage.Height);
        Assert.Equal(saved, files.Storage.Exists(addedImage.StorageKey));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(files.Root, ".tmp")));
        Assert.True(source.CanRead);
    }

    private static IEnumerable<object> ExtractCandidates(string html)
    {
        var parser = new AppImageParserService(new Mock<IRepositoryManager>().Object, new Mock<IImageStager>().Object, Options.Create(new ParserSettings()), TimeProvider.System,
            NullLogger<AppImageParserService>.Instance);
        var document = new HtmlParser().ParseDocument(html);
        var method = typeof(AppImageParserService).GetMethod("ExtractCandidates", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((IEnumerable)method.Invoke(parser, [document, "https://source.example"])!).Cast<object>().ToList();
    }
}
