using System.Diagnostics;
using Contracts;
using Entities.Models;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Repository;

namespace GallerySiteIntegrationTests;

public sealed partial class ForMeFeedPerformanceTests
{
    [ImageRepositoryPerformanceFact]
    public async Task ContentVisibility_CompareFilteringAndProfileOptIn()
    {
        const int imageCount = 17_000;
        var clock = new TestClock();
        var user = (await SeedAsync(clock, imageCount))[0];
        var viewer = new ImageViewer(user.Id, false);
        await using var context = database.CreateContext();
        var images = new AppImageRepository(context);
        var discovery = new DiscoveryRepository(context);
        var feeds = FeedRepository(context, clock);
        var now = clock.GetUtcNow();

        async Task Measure(string scenario, int expected)
        {
            await Time("search", async () => Assert.Equal(expected,
                (await images.SearchAsync(new(null), viewer)).Total));
            await Time("discovery", async () => Assert.Equal(expected,
                (await discovery.GetCollectionAsync(DiscoveryCollection.NewUploads, now.AddDays(-1),
                    now.AddDays(1), user.Id, 1, 20)).Total));
            await Time("direct metadata", async () =>
                Assert.NotNull(await images.GetReadableMetadataByIdAsync(1, viewer)));
            await Time("ForMe generation + first page", async () =>
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var id = await feeds.CreateAsync(user.Id, 20, new([], []), deadline.Token);
                Assert.Equal(20, (await feeds.ReadAsync(id, viewer, 0)).Images.Count);
            });

            async Task Time(string operation, Func<Task> action)
            {
                await action();
                var samples = new List<double>();
                for (var i = 0; i < 5; i++)
                {
                    var start = Stopwatch.GetTimestamp();
                    await action();
                    samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                }

                output.WriteLine($"{scenario}, {operation}: median {samples.Order().ElementAt(2):F2} ms");
            }
        }

        var single = await context.Tags.SingleAsync();
        var even = new ImageTag
            { Name = "even", NormalizedName = "even", ModerationStatus = TagModerationStatus.Approved };
        var fifth = new ImageTag
            { Name = "fifth", NormalizedName = "fifth", ModerationStatus = TagModerationStatus.Approved };
        context.Tags.AddRange(even, fifth);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
                                                            INSERT INTO "AppImageImageTag" ("AppImagesId", "TagsId")
                                                            SELECT "Id", {even.Id} FROM "Images" WHERE "Id" % 2 = 0
                                                            UNION ALL SELECT "Id", {fifth.Id} FROM "Images" WHERE "Id" % 5 = 0
                                                            """);
        await context.Database.ExecuteSqlRawAsync("ANALYZE");
        await Measure("no rules", imageCount);

        var rules = new ContentVisibilityRuleRepository(context);
        await rules.SaveAsync(null, [single.Id], default);
        await rules.SaveAsync(null, [even.Id, fifth.Id], default);
        try
        {
            // The existing fixture attaches the singleton to every third image.
            await Measure("filtered", imageCount - imageCount / 3 - imageCount / 10 + imageCount / 30);
            await context.AppUsers.Where(row => row.Id == user.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.DisplayAllContent, true));
            await Measure("opted in", imageCount);
        }
        finally
        {
            await context.ContentVisibilityRules.ExecuteDeleteAsync();
        }
    }
}