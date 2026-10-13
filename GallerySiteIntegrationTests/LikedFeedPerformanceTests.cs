using System.Text.RegularExpressions;
using Contracts;
using Entities.Models;
using Repository;

namespace GallerySiteIntegrationTests;

public sealed class LikedFeedPerformanceTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task GetLikedByUserAsync_MixedImages_PreservesOrderingFiltersPaginationAndViewerCards()
    {
        var (ownerId, feedUserId, ids) = await SeedAsync();
        var capture = new QueryCapture();
        await using var context = database.CreateContext(capture);
        var repository = new AppImageRepository(context);
        var owner = new ImageViewer(ownerId, false);
        var orderedIndices = Enumerable.Range(5, 55).OrderByDescending(index => index % 12 / 3)
            .ThenByDescending(index => index).ToArray();
        var expected = orderedIndices.Select(index => ids[index]).ToArray();

        var page = await repository.GetLikedByUserAsync(feedUserId, 0, int.MaxValue, owner);
        Assert.Equal(55, page.Total);
        Assert.Equal(expected.Take(50), page.Images.Select(image => image.Id));
        var first = page.Images[0];
        Assert.True(first.IsLikedByCurrentUser);
        Assert.True(first.CanSeePendingTags);
        Assert.Equal(2, first.LikeCount);
        Assert.Equal(1, first.CommentCount);
        Assert.Equal(new[] { "approved", "pending" }, first.Tags.Select(tag => tag.Name));
        Assert.All(page.Images.Skip(1), image => Assert.False(image.IsLikedByCurrentUser));
        Assert.Equal(expected.Skip(50), (await repository.GetLikedByUserAsync(feedUserId, 2, 50, owner))
            .Images.Select(image => image.Id));
        Assert.Equal(expected[0],
            Assert.Single((await repository.GetLikedByUserAsync(feedUserId, -1, 0, owner)).Images).Id);

        capture.Clear();
        var extreme = await repository.GetLikedByUserAsync(feedUserId, int.MaxValue, 50, owner);
        Assert.Equal(55, extreme.Total);
        Assert.Empty(extreme.Images);
        Assert.Single(capture.Reads); // Overflow-safe empty page needs only the count.

        foreach (var viewer in new[] { new ImageViewer(null, false), new ImageViewer(feedUserId, true) })
        {
            var result = await repository.GetLikedByUserAsync(feedUserId, 1, 1, viewer);
            var card = Assert.Single(result.Images);
            Assert.Equal(viewer.UserId == feedUserId, card.IsLikedByCurrentUser);
            Assert.Equal(viewer.IsStaff, card.CanSeePendingTags);
            Assert.Equal(viewer.IsStaff ? new[] { "approved", "pending" } : new[] { "approved" },
                card.Tags.Select(tag => tag.Name));
        }

        var filtered = await repository.GetLikedByUserAsync(feedUserId, 1, 50, owner,
            aiUsage: [AiUsageClassification.AiGenerated, AiUsageClassification.AiGenerated]);
        Assert.Equal(27, filtered.Total);
        Assert.Equal(orderedIndices.Where(index => index % 2 == 0).Select(index => ids[index]),
            filtered.Images.Select(image => image.Id));
        var noMatches = await repository.GetLikedByUserAsync(feedUserId, 1, 50, owner,
            aiUsage: [AiUsageClassification.AiAssisted]);
        Assert.Equal(0, noMatches.Total);
        Assert.Empty(noMatches.Images);
        Assert.Equal(55, (await repository.GetLikedByUserAsync(feedUserId, 1, 50, owner, aiUsage: [])).Total);
        Assert.Empty((await repository.GetLikedByUserAsync(int.MaxValue, 1, 50, owner)).Images);
        Assert.Empty(capture.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetLikedByUserAsync_PageQuery_DoesNotRepeatTimestampLookupForEveryCandidate()
    {
        var (_, feedUserId, _) = await SeedAsync();
        var capture = new QueryCapture();
        await using var context = database.CreateContext(capture);
        var result = await new AppImageRepository(context).GetLikedByUserAsync(feedUserId, 1, 2, new(null, false));
        Assert.Equal(2, result.Images.Count);
        Assert.Equal(2, capture.Reads.Count);
        var pageSql = capture.Reads[1];
        Assert.Contains("LIMIT", pageSql);
        // Engagement count subqueries are expected, but sorting must use the joined like timestamp.
        // The former ORDER BY (SELECT ... CreatedAtUtc ...) ran once per matching image before LIMIT.
        Assert.False(Regex.IsMatch(pageSql, @"ORDER BY\s+\(\s*SELECT[^)]*""CreatedAtUtc""",
            RegexOptions.Singleline | RegexOptions.IgnoreCase), pageSql);
        Assert.DoesNotContain("\"Embedding\"", pageSql);
        Assert.DoesNotContain("\"PasswordHash\"", pageSql);
        Assert.Empty(capture.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetLikedByUserAsync_CancelledToken_PropagatesCancellation()
    {
        await using var context = database.CreateContext();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AppImageRepository(context)
            .GetLikedByUserAsync(1, 1, 50, new(null, false), cancellation.Token));
    }

    private async Task<(int OwnerId, int FeedUserId, int[] Ids)> SeedAsync()
    {
        await using var context = database.CreateContext();
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        AppUser User() => new()
        {
            Login = "liked-" + Guid.NewGuid().ToString("N"), NormalizedLogin = Guid.NewGuid().ToString("N"),
            PasswordHash = "unused", CreatedAtUtc = now
        };

        var owner = User();
        var feedUser = User();
        context.AppUsers.AddRange(owner, feedUser);
        await context.SaveChangesAsync();
        var tags = new[] { TagModerationStatus.Approved, TagModerationStatus.Pending, TagModerationStatus.Rejected }
            .Select(status => new ImageTag
            {
                Name = status.ToString().ToLowerInvariant(), NormalizedName = Guid.NewGuid().ToString("N"),
                ModerationStatus = status, CreatedAtUtc = now
            }).ToArray();
        var images = Enumerable.Range(0, 60).Select(index => new UserMadeImage
        {
            Source = ImageSource.UserUpload, UploadedById = owner.Id, UploadedAtUtc = now.AddMinutes(-index),
            Visibility = index == 0 ? ImageVisibility.Private : ImageVisibility.Gallery,
            ModerationStatus = index == 1 ? ModerationStatus.Pending :
                index == 2 ? ModerationStatus.Rejected : ModerationStatus.Approved,
            DeletedAtUtc = index == 3 ? now : null, AiUsage = index % 2 == 0
                ? AiUsageClassification.AiGenerated
                : AiUsageClassification.HumanMade,
            StorageKey = "synthetic/" + Guid.NewGuid().ToString("N"), ContentType = "image/jpeg",
            Width = 100, Height = 80, Tags = tags.ToList(),
            // Repeated timestamp groups require both time ordering and the image-ID tiebreaker.
            Likes = index == 4
                ? []
                : [new ImageLike { UserId = feedUser.Id, CreatedAtUtc = now.AddSeconds(Math.Floor(index % 12 / 3d)) }]
        }).ToArray();
        images[^1].Likes.Add(new ImageLike { UserId = owner.Id, CreatedAtUtc = now.AddDays(-1) });
        images[^1].Comments =
        [
            new Comment { AuthorId = owner.Id, Content = "visible", CreatedAtUtc = now },
            new Comment { AuthorId = owner.Id, Content = "deleted", CreatedAtUtc = now, DeletedAtUtc = now }
        ];
        context.Images.AddRange(images.AsEnumerable());
        await context.SaveChangesAsync();
        return (owner.Id, feedUser.Id, images.Select(image => image.Id).ToArray());
    }
}