using System.Data.Common;
using Contracts;
using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Repository;

namespace GallerySiteIntegrationTests;

public sealed partial class ForMeVectorSearchTests
{
    private const string ResolutionTag = "ForMeExcludedTagResolution";

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Create_MixedExcludedTags_ResolvesOnlyApprovedNamesWithoutChangingStoredFilters(int references)
    {
        var user = await SeedAsync(references, 40);
        await using var setup = database.CreateContext();
        var candidates = await setup.Images.Where(image => !image.Likes.Any(like => like.UserId == user))
            .OrderBy(image => image.Id).Take(3).ToArrayAsync();
        setup.Tags.AddRange(Tag("approved", TagModerationStatus.Approved, candidates[0]),
            Tag("pending", TagModerationStatus.Pending, candidates[1]), Tag("rejected", TagModerationStatus.Rejected, candidates[2]));
        await setup.SaveChangesAsync();
        var names = new[] { "approved", "unknown", "pending", "rejected" };
        var usage = new[] { (int)AiUsageClassification.HumanMade };
        var originalNames = names.ToArray();
        var originalUsage = usage.ToArray();
        var capture = new QueryCapture();
        var logger = new SearchLogger();
        await using var context = database.CreateContext(capture);
        var id = await Repository(context, logger: logger).CreateAsync(user, 50, new(usage, names));
        var query = Assert.Single(capture.Reads, sql => sql.Contains(ResolutionTag));
        Assert.Matches("SELECT [a-zA-Z0-9_]+\\.\"NormalizedName\"\\r?\\nFROM", query);
        Assert.DoesNotContain("\"Id\"", query);
        Assert.DoesNotContain(capture.Entities, entity => entity is ImageTag);
        Assert.Equal(1, Assert.Single(logger.Events)["ResolvedExcludedTagCount"]);
        Assert.Equal(originalNames, names);
        Assert.Equal(originalUsage, usage);
        var feed = await context.ForMeFeeds.AsNoTracking().SingleAsync(feed => feed.Id == id);
        Assert.Equal(originalNames, feed.ExcludedTags);
        Assert.Equal(originalUsage, feed.AiUsage);
        var ids = await context.ForMeFeedEntries.Where(entry => entry.FeedId == id).Select(entry => entry.ImageId).ToArrayAsync();
        Assert.DoesNotContain(candidates[0].Id, ids);
        Assert.Contains(candidates[1].Id, ids);
        Assert.Contains(candidates[2].Id, ids);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    public async Task Create_ExcludedTagResolution_ProjectsAtMostRequestedNamesAndSkipsEmptyRequest(int count)
    {
        var user = await SeedAsync(1, 30);
        var names = Enumerable.Range(0, count).Select(index => "resolution-bound-" + index).ToArray();
        await using var setup = database.CreateContext();
        setup.Tags.AddRange(names.Select(name => Tag(name, TagModerationStatus.Approved)));
        await setup.SaveChangesAsync();
        var capture = new QueryCapture();
        var logger = new SearchLogger();
        await using var context = database.CreateContext(capture);
        await Repository(context, logger: logger).CreateAsync(user, 20, new([], names));
        Assert.Equal(count == 0 ? 0 : 1, capture.Reads.Count(sql => sql.Contains(ResolutionTag)));
        Assert.Equal(count, Assert.Single(logger.Events)["ResolvedExcludedTagCount"]);
        Assert.DoesNotContain(capture.Entities, entity => entity is ImageTag);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_TagChangesAfterGenerationSnapshot_UsesSnapshotButPagesKeepOriginalFilter(bool existedPending)
    {
        var user = await SeedAsync(1, 10);
        var name = existedPending ? "snapshot-pending-tag" : "snapshot-new-tag";
        int imageId;
        await using (var setup = database.CreateContext())
        {
            var image = await setup.Images.Where(image => !image.Likes.Any(like => like.UserId == user)).OrderBy(image => image.Id).FirstAsync();
            imageId = image.Id;
            if (existedPending)
            {
                setup.Tags.Add(Tag(name, TagModerationStatus.Pending, image));
                await setup.SaveChangesAsync();
            }
        }
        var mutation = new BeforeResolution(async () =>
        {
            await using var writer = database.CreateContext();
            if (existedPending)
                (await writer.Tags.SingleAsync(tag => tag.NormalizedName == name)).ModerationStatus = TagModerationStatus.Approved;
            else
                writer.Tags.Add(Tag(name, TagModerationStatus.Approved, (await writer.Images.FindAsync(imageId))!));
            await writer.SaveChangesAsync();
        });
        var capture = new QueryCapture();
        var costs = new PlannerCostCapture();
        var logger = new SearchLogger();
        await using var context = database.CreateContext(mutation, capture, costs);
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("SET random_page_cost = 2.3");
        try
        {
            var repository = Repository(context, logger: logger);
            var originalNames = new[] { name };
            var id = await repository.CreateAsync(user, 50, new([], originalNames));
            Assert.True(mutation.Applied);
            var created = Assert.Single(logger.Events);
            Assert.Equal(0, created["ResolvedExcludedTagCount"]);
            Assert.Equal(1000, created["AnnCandidateLimit"]);
            Assert.False(created.ContainsKey("AppliedAnnRandomPageCost"));
            Assert.All(costs.Values, value => Assert.Equal("2.3", value.Cost));
            Assert.DoesNotContain("NormalizedName", Assert.Single(capture.Reads, sql => sql.Contains(ForMeFeedRepository.AnnQueryTag)));
            Assert.True(await context.ForMeFeedEntries.AnyAsync(entry => entry.FeedId == id && entry.ImageId == imageId));
            var page = await repository.ReadAsync(id, new(user, false), 0);
            Assert.Equal(originalNames, page.Feed.ExcludedTags);
            Assert.DoesNotContain(page.Images, image => image.Id == imageId);
            Assert.False(logger.Events[^1].ContainsKey("ResolvedExcludedTagCount"));
        }
        finally { await context.Database.ExecuteSqlRawAsync("RESET random_page_cost"); }
    }

    private static ImageTag Tag(string name, TagModerationStatus status, params AppImage[] images) => new()
    {
        Name = name, NormalizedName = name, ModerationStatus = status,
        CreatedAtUtc = DateTimeOffset.UtcNow, AppImages = images.ToList()
    };

    private sealed class BeforeResolution(Func<Task> action) : DbCommandInterceptor
    {
        public bool Applied { get; private set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!Applied && command.CommandText.Contains(ResolutionTag))
            {
                await action();
                Applied = true;
            }
            return result;
        }
    }
}
