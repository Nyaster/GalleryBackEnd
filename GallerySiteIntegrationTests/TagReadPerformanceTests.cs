using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Repository;
using Shared.DataTransferObjects;
using Xunit.Abstractions;
using static GallerySiteIntegrationTests.ImageRepositoryPerformanceSupport;

namespace GallerySiteIntegrationTests;

[Collection("Image repository performance")]
public sealed class TagReadPerformanceTests(PostgresFixture database, ITestOutputHelper output)
    : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task GetTags_FilteredPage_CountsAllLinkedImagesWithoutMaterializingEntities()
    {
        await SeedAsync(4, 3);
        var capture = new QueryCapture();
        var metrics = new ImageRepositoryReadMetrics();
        await using var context = database.CreateContext(capture, metrics);
        var repository = new AppImageRepository(context);

        var page = await repository.GetTagsAsync(TagModerationStatus.Pending, 2, 2);

        Assert.Equal(4, page.Total);
        Assert.Equal(new[] { "tag-02", "tag-03" }, page.Tags.Select(tag => tag.Name));
        Assert.All(page.Tags, tag => Assert.Equal(3, tag.LinkedImageCount));
        Assert.Empty(capture.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(2, capture.Reads.Count);
        Assert.DoesNotContain("\"Embedding\"", capture.Reads[1]);
        Assert.Equal(2, metrics.Reads[1].Rows);
        Assert.Equal(5, metrics.Reads[1].Columns);
        Assert.Contains("count(*)", capture.Reads[1]);

        var first = await repository.GetTagsAsync(TagModerationStatus.Pending, int.MinValue, 0);
        Assert.Equal("tag-00", Assert.Single(first.Tags).Name);
        var clamped = await repository.GetTagsAsync(TagModerationStatus.Pending, 1, int.MaxValue);
        Assert.Equal(4, clamped.Tags.Count);
        capture.Clear();
        Assert.Empty((await repository.GetTagsAsync(TagModerationStatus.Pending, int.MaxValue, 50)).Tags);
        Assert.Single(capture.Reads); // Overflow-safe empty page avoids a data read.
        Assert.Empty((await repository.GetTagsAsync(TagModerationStatus.Rejected, 1, 50)).Tags);
    }

    [Fact]
    public async Task GetTags_OversizedPage_ClampsToFiftyAndCountsUnlinkedTagsAsZero()
    {
        await SeedAsync(60, 0);
        await using var context = database.CreateContext();

        var result = await new AppImageRepository(context).GetTagsAsync(TagModerationStatus.Pending, 1, int.MaxValue);

        Assert.Equal(60, result.Total);
        Assert.Equal(50, result.Tags.Count);
        Assert.All(result.Tags, tag => Assert.Equal(0, tag.LinkedImageCount));
    }

    [ImageRepositoryPerformanceFact]
    public async Task GetTags_SyntheticDataset_ReportsBeforeAndAfter()
    {
        await SeedAsync(24, 80);
        output.WriteLine("tags dataset: 24 pending tags with 80 linked images each (1024-float vectors); page size=10");
        foreach (var baseline in new[] { true, false })
        {
            var capture = new QueryCapture();
            var metrics = new ImageRepositoryReadMetrics();
            await using (var context = database.CreateContext(capture, metrics))
            {
                var result = await ReadAsync(context, baseline);
                Assert.Equal(24, result.Total);
                Assert.Equal(10, result.Tags.Count);
                Assert.All(result.Tags, tag => Assert.Equal(80, tag.LinkedImageCount));
                output.WriteLine($"tags {(baseline ? "before" : "after")}: entities={capture.Entities.Count} " +
                    $"rows=[{string.Join(',', metrics.Reads.Select(read => read.Rows))}] " +
                    $"columns=[{string.Join(',', metrics.Reads.Select(read => read.Columns))}]");
            }
            await MeasureAsync($"tags {(baseline ? "before" : "after")}", async () =>
            {
                await using var context = database.CreateContext();
                await ReadAsync(context, baseline);
            }, null, output);
        }
    }

    private static async Task<(List<AdminTagDto> Tags, int Total)> ReadAsync(RepositoryContext context, bool baseline)
    {
        if (!baseline) return await new AppImageRepository(context).GetTagsAsync(TagModerationStatus.Pending, 1, 10);
        var query = context.Tags.AsNoTracking().Where(tag => tag.ModerationStatus == TagModerationStatus.Pending)
            .OrderBy(tag => tag.CreatedAtUtc).ThenBy(tag => tag.Id);
        var total = await query.CountAsync();
        var tags = await query.Include(tag => tag.AppImages).Skip(0).Take(10).ToListAsync();
        return (tags.Select(tag => new AdminTagDto(tag.Id, tag.Name, tag.ModerationStatus, tag.CreatedAtUtc,
            tag.AppImages.Count)).ToList(), total);
    }

    private async Task SeedAsync(int tagCount, int imagesPerTag)
    {
        await ResetAsync(database);
        await using var context = database.CreateContext();
        for (var i = 0; i < tagCount; i++)
        {
            var tag = new ImageTag
            {
                Name = $"tag-{i:D2}", NormalizedName = $"tag-{i:D2}", CreatedAtUtc = Now,
                ModerationStatus = TagModerationStatus.Pending
            };
            for (var j = 0; j < imagesPerTag; j++)
            {
                var image = Image(i * imagesPerTag + j);
                image.Visibility = j % 3 == 0 ? ImageVisibility.Private : ImageVisibility.Gallery;
                image.DeletedAtUtc = j % 3 == 1 ? Now : null;
                image.ModerationStatus = j % 3 == 2 ? ModerationStatus.Rejected : ModerationStatus.Approved;
                tag.AppImages.Add(image);
            }
            context.Tags.Add(tag);
        }
        context.Tags.Add(new ImageTag
        {
            Name = "approved", NormalizedName = "approved", CreatedAtUtc = Now.AddDays(-1),
            ModerationStatus = TagModerationStatus.Approved
        });
        await context.SaveChangesAsync();
    }
}
