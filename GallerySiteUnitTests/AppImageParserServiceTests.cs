using AngleSharp.Html.Parser;
using Contracts;
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

    private static IEnumerable<object> ExtractCandidates(string html)
    {
        var parser = new AppImageParserService(new Mock<IRepositoryManager>().Object, new Mock<IImageStorage>().Object,
            new Mock<IImageProcessor>().Object, Options.Create(new ParserSettings()), TimeProvider.System,
            NullLogger<AppImageParserService>.Instance);
        var document = new HtmlParser().ParseDocument(html);
        var method = typeof(AppImageParserService).GetMethod("ExtractCandidates", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((IEnumerable)method.Invoke(parser, [document, "https://source.example"])!).Cast<object>().ToList();
    }
}
