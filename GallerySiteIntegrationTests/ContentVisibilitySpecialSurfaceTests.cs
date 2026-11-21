using Contracts;
using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pgvector;
using Repository;
using Shared.Configuration;

namespace GallerySiteIntegrationTests;

public sealed class ContentVisibilitySpecialSurfaceTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task DiscoveryAndRankings_FilterBeforePagingAndPreserveGlobalRanks()
    {
        await using var context = database.CreateContext();
        var seed = await SeedAsync(context);
        foreach (var image in seed.Images)
            context.ImageLikeActivities.Add(new ImageLikeActivity
            {
                ImageId = image.Id, UserId = seed.User.Id, Type = LikeActivityType.Like, OccurredAtUtc = seed.Now
            });
        await context.SaveChangesAsync();

        var discovery = new DiscoveryRepository(context);
        var rankings = new RankingRepository(context);
        var end = seed.Now.AddDays(1);
        Assert.True(await rankings.TryCreateSnapshotAsync(RankingPeriod.Daily, seed.Now, end, end));
        var archive = await rankings.GetSnapshotAsync(RankingPeriod.Daily, seed.Now);
        Assert.NotNull(archive);

        foreach (var viewer in new[] { new ImageViewer(seed.User.Id, false), default(ImageViewer) })
        {
            var discovered = await discovery.GetCollectionAsync(DiscoveryCollection.NewUploads, seed.Now, end,
                viewer.UserId, 1, 1);
            Assert.Equal(2, discovered.GalleryTotal);
            Assert.Equal(2, discovered.Total);
            Assert.Equal(seed.Images[1].Id, Assert.Single(discovered.Entries).Image.Id);

            var live = await rankings.GetLivePageAsync(seed.Now, end, 1, 1, viewer, null);
            var archived = await rankings.GetArchivePageAsync(archive.Id, 1, 1, viewer, null);
            foreach (var page in new[] { live, archived })
            {
                Assert.Equal(2, page.Total);
                var entry = Assert.Single(page.Entries);
                Assert.Equal(2, entry.Rank);
                Assert.Equal(seed.Images[1].Id, entry.Image.Id);
            }
        }
    }

    [Fact]
    public async Task ForMe_UsesCurrentRulesForSeedsCandidatesAndExistingPages()
    {
        await using var context = database.CreateContext();
        var seed = await SeedAsync(context, displayAllContent: true);
        var viewer = new ImageViewer(seed.User.Id, false);
        var hiddenSeed = seed.Images[0];
        var visibleCandidate = seed.Images[1];
        var hiddenCandidate = seed.Images[2];

        var triggerTag = Assert.Single(hiddenCandidate.Tags, tag => tag.NormalizedName != "sfw");
        hiddenSeed.Tags.Add(triggerTag);
        context.ImageLikes.Add(new ImageLike
        {
            ImageId = hiddenSeed.Id, UserId = seed.User.Id, CreatedAtUtc = seed.Now
        });
        await context.SaveChangesAsync();

        var repository = new ForMeFeedRepository(context, new TestClock(), NullLogger<ForMeFeedRepository>.Instance,
            Options.Create(new ForMeFeedOptions()));
        var filters = new ForMeFilters([], []);
        var existingId = await repository.CreateAsync(seed.User.Id, 1, filters);
        var unfiltered = await repository.ReadAsync(existingId, viewer, 0);
        Assert.Equal("Personalized", unfiltered.Feed.Mode);
        Assert.Equal(2, unfiltered.Feed.Total);
        Assert.Equal(hiddenCandidate.Id, Assert.Single(unfiltered.Images).Id);
        Assert.True(unfiltered.HasMore);

        seed.User.DisplayAllContent = false;
        await context.SaveChangesAsync();

        var restricted = await repository.ReadAsync(existingId, viewer, 0);
        Assert.Equal(2, restricted.Feed.Total);
        Assert.Equal(2, restricted.LastRank);
        Assert.Equal(visibleCandidate.Id, Assert.Single(restricted.Images).Id);
        Assert.False(restricted.HasMore);

        var newId = await repository.CreateAsync(seed.User.Id, 1, filters);
        var fallback = await repository.ReadAsync(newId, viewer, 0);
        Assert.Equal("Fallback", fallback.Feed.Mode);
        Assert.Equal(1, fallback.Feed.Total);
        Assert.Equal(visibleCandidate.Id, Assert.Single(fallback.Images).Id);

        // A visible seed restores personalization, while the hidden candidate remains excluded at generation.
        var secondVisible = Image(seed.User.Id, seed.Now.AddMinutes(3), 20);
        context.Images.Add(secondVisible);
        context.ImageLikes.Add(new ImageLike
        {
            ImageId = visibleCandidate.Id, UserId = seed.User.Id, CreatedAtUtc = seed.Now
        });
        await context.SaveChangesAsync();

        var personalizedId = await repository.CreateAsync(seed.User.Id, 1, filters);
        var personalized = await repository.ReadAsync(personalizedId, viewer, 0);
        Assert.Equal("Personalized", personalized.Feed.Mode);
        Assert.Equal(1, personalized.Feed.Total);
        Assert.Equal(secondVisible.Id, Assert.Single(personalized.Images).Id);
    }

    private static async Task<Seed> SeedAsync(RepositoryContext context, bool displayAllContent = false)
    {
        await context.ContentVisibilityRules.ExecuteDeleteAsync();
        await context.Images.ExecuteDeleteAsync();
        await context.Tags.ExecuteDeleteAsync();
        var now = new TestClock().GetUtcNow();
        var user = new AppUser
        {
            Login = "visibility-reader", NormalizedLogin = Guid.NewGuid().ToString("N"), PasswordHash = "unused",
            CreatedAtUtc = now, DisplayAllContent = displayAllContent
        };
        var sfw = new ImageTag { Name = "sfw", NormalizedName = "sfw", ModerationStatus = TagModerationStatus.Approved };
        var gated = new ImageTag
        {
            Name = "restricted", NormalizedName = "restricted", ModerationStatus = TagModerationStatus.Approved
        };
        context.AppUsers.Add(user);
        context.Tags.AddRange(sfw, gated);
        await context.SaveChangesAsync();
        var images = new[]
        {
            Image(user.Id, now, 0), Image(user.Id, now.AddMinutes(1), 10), Image(user.Id, now.AddMinutes(2), 1)
        };
        foreach (var image in images)
            image.Tags.Add(sfw);
        images[2].Tags.Add(gated);
        context.Images.AddRange(images);
        context.ContentVisibilityRules.Add(new ContentVisibilityRule
        {
            TagGroupKey = gated.Id.ToString(), TagLinks = [new ContentVisibilityRuleTag { TagId = gated.Id }]
        });
        await context.SaveChangesAsync();
        return new Seed(now, user, images);
    }

    private static UserMadeImage Image(int userId, DateTimeOffset uploadedAt, float distance)
    {
        var values = new float[1024];
        values[0] = distance;
        return new UserMadeImage
        {
            UploadedById = userId, UploadedAtUtc = uploadedAt, Source = ImageSource.UserUpload,
            Visibility = ImageVisibility.Gallery, ModerationStatus = ModerationStatus.Approved,
            Embedding = new Vector(values), EmbeddingStatus = EmbeddingStatus.Ready,
            StorageKey = Guid.NewGuid().ToString("N"), ContentType = "image/jpeg", Width = 1, Height = 1
        };
    }

    private sealed record Seed(DateTimeOffset Now, AppUser User, UserMadeImage[] Images);
}
