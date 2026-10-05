using Application.BackgroundService;
using Application.Features.Rankings;
using Contracts;
using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Repository;
using Service.Contracts;

namespace GallerySiteIntegrationTests;

public sealed class RankingRepositoryTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task LiveRankings_TiesFiltersAndPaging_PreserveGlobalRanksWithoutLoadingOtherCards()
    {
        var seed = await SeedAsync();
        var capture = new QueryCapture();
        await using var context = database.CreateContext(capture);
        var repository = new RankingRepository(context);
        var viewer = new ImageViewer(seed.OwnerId, false);
        var first = await repository.GetLivePageAsync(seed.Start, seed.Start.AddDays(1), 1, 2, viewer, null);
        Assert.Equal(44, first.Total);
        Assert.Equal(new[] { seed.WinnerId, seed.HighTieId }, first.Entries.Select(entry => entry.Image.Id));
        Assert.Equal(new[] { 1, 2 }, first.Entries.Select(entry => entry.Rank));
        Assert.Equal(new[] { 3, 2 }, first.Entries.Select(entry => entry.LikeDelta));
        Assert.Equal(1, first.Entries[0].Image.LikeCount);
        Assert.Equal(1, first.Entries[0].Image.CommentCount);
        Assert.True(first.Entries[0].Image.IsLikedByCurrentUser);
        Assert.True(first.Entries[0].Image.CanSeePendingTags);
        Assert.Equal(3, capture.Reads.Count);
        Assert.Contains("ROW_NUMBER()", capture.Reads[1]);
        Assert.Contains("LIMIT", capture.Reads[1]);
        Assert.Contains(capture.IdParameters, ids => ids.SequenceEqual(new[] { seed.WinnerId, seed.HighTieId }));
        Assert.Empty(capture.Entities);
        Assert.All(capture.Reads, sql => Assert.DoesNotContain("\"Embedding\"", sql));

        capture.Clear();
        var filtered = await repository.GetLivePageAsync(seed.Start, seed.Start.AddDays(1), 2, 1, viewer,
            [AiUsageClassification.HumanMade]);
        Assert.Equal(3, filtered.Total);
        Assert.Equal(seed.LowTieId, Assert.Single(filtered.Entries).Image.Id);
        Assert.Equal(3, filtered.Entries[0].Rank);
        Assert.Contains(capture.IdParameters, ids => ids.SequenceEqual(new[] { seed.LowTieId }));
        Assert.Empty(capture.Entities);

        var allHuman = await repository.GetLivePageAsync(seed.Start, seed.Start.AddDays(1), 1, 50, viewer,
            [AiUsageClassification.HumanMade]);
        Assert.Equal(new[] { 1, 3, 4 }, allHuman.Entries.Select(entry => entry.Rank));
        capture.Clear();
        var beyond = await repository.GetLivePageAsync(seed.Start, seed.Start.AddDays(1), int.MaxValue, 50, viewer, null);
        Assert.Equal(44, beyond.Total);
        Assert.Empty(beyond.Entries);
        Assert.Single(capture.Reads);
        var empty = await repository.GetLivePageAsync(seed.Start.AddDays(2), seed.Start.AddDays(3), 1, 20, viewer, null);
        Assert.Equal(0, empty.Total);
        Assert.Empty(empty.Entries);
        Assert.Empty(context.ChangeTracker.Entries());

        var handler = new GetRankingsHandler(new RepositoryManager(context), new Viewer(seed.OwnerId),
            new FixedClock(seed.Start.AddHours(12)));
        var response = await handler.Handle(new GetRankingsCommand(RankingPeriod.Daily, null, Page: 0, PageSize: 500), CancellationToken.None);
        Assert.Equal(1, response.Page);
        Assert.Equal(50, response.PageSize);
        Assert.Equal(44, response.Total);
        Assert.Equal(new[] { "approved" }, response.Entries[0].Image.Tags);
        Assert.Equal(new[] { "pending" }, response.Entries[0].Image.PendingTags);
        Assert.Equal("/api/images/" + seed.WinnerId + "/content", response.Entries[0].Image.ContentUrl);
    }

    [Fact]
    public async Task ArchivedRankings_VisibilityAndAiChanges_KeepStoredRanksAndExactTotals()
    {
        var seed = await SeedAsync();
        await using (var context = database.CreateContext())
        {
            var repository = new RankingRepository(context);
            Assert.True(await repository.TryCreateSnapshotAsync(RankingPeriod.Daily, seed.Start, seed.Start.AddDays(1), seed.Start.AddDays(2)));
            Assert.False(await repository.TryCreateSnapshotAsync(RankingPeriod.Daily, seed.Start, seed.Start.AddDays(1), seed.Start.AddDays(2)));
            await context.Images.Where(image => image.Id == seed.WinnerId)
                .ExecuteUpdateAsync(update => update.SetProperty(image => image.DeletedAtUtc, seed.Start.AddDays(1)));
            await context.Images.Where(image => image.Id == seed.LowTieId)
                .ExecuteUpdateAsync(update => update.SetProperty(image => image.AiUsage, AiUsageClassification.AiGenerated));
            Assert.Empty(context.ChangeTracker.Entries());
        }
        var capture = new QueryCapture();
        await using var readContext = database.CreateContext(capture);
        var rankings = new RankingRepository(readContext);
        var snapshot = (await rankings.GetSnapshotAsync(RankingPeriod.Daily, seed.Start))!;
        Assert.Single(capture.Reads);
        Assert.DoesNotContain("RankingSnapshotEntries", capture.Reads[0]);
        capture.Clear();
        var filtered = await rankings.GetArchivePageAsync(snapshot.Id, 1, 20, new ImageViewer(seed.OwnerId, false),
            [AiUsageClassification.HumanMade]);
        Assert.Equal(1, filtered.Total);
        Assert.Equal(seed.OlderTieId, Assert.Single(filtered.Entries).Image.Id);
        Assert.Equal(4, filtered.Entries[0].Rank);
        Assert.Equal(3, capture.Reads.Count);
        Assert.Contains(capture.IdParameters, ids => ids.SequenceEqual(new[] { seed.OlderTieId }));
        Assert.Empty(capture.Entities);
        var first = await rankings.GetArchivePageAsync(snapshot.Id, 1, 1, default, null);
        Assert.Equal(43, first.Total);
        Assert.Equal(2, Assert.Single(first.Entries).Rank);
        var beyond = await rankings.GetArchivePageAsync(snapshot.Id, int.MaxValue, 50, default, null);
        Assert.Equal(43, beyond.Total);
        Assert.Empty(beyond.Entries);
        Assert.Null(await rankings.GetSnapshotAsync(RankingPeriod.Daily, seed.Start.AddDays(-100)));
    }

    [Fact]
    public async Task SnapshotCreation_ConcurrentWorkers_CommitOneCompleteSnapshotAndEmptyPeriods()
    {
        var seed = await SeedAsync();
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var created = await Task.WhenAll(
            new RankingRepository(first).TryCreateSnapshotAsync(RankingPeriod.Daily, seed.Start, seed.Start.AddDays(1), seed.Start.AddDays(2)),
            new RankingRepository(second).TryCreateSnapshotAsync(RankingPeriod.Daily, seed.Start, seed.Start.AddDays(1), seed.Start.AddDays(2)));
        Assert.Single(created, value => value);
        await using var verify = database.CreateContext();
        var snapshot = await verify.RankingSnapshots.Include(value => value.Entries).SingleAsync();
        Assert.Equal(44, snapshot.Entries.Count);
        Assert.Equal(Enumerable.Range(1, 44), snapshot.Entries.Select(entry => entry.Rank).Order());
        Assert.Equal(seed.WinnerId, snapshot.Entries.Single(entry => entry.Rank == 1).ImageId);
        Assert.Empty(first.ChangeTracker.Entries());
        Assert.Empty(second.ChangeTracker.Entries());
        var repository = new RankingRepository(first);
        Assert.True(await repository.TryCreateSnapshotAsync(RankingPeriod.Daily, seed.Start.AddDays(3), seed.Start.AddDays(4), seed.Start.AddDays(5)));
        var empty = await verify.RankingSnapshots.AsNoTracking().Include(value => value.Entries)
            .SingleAsync(value => value.PeriodStartUtc == seed.Start.AddDays(3));
        Assert.Empty(empty.Entries);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.TryCreateSnapshotAsync(RankingPeriod.Daily,
            seed.Start.AddDays(6), seed.Start.AddDays(7), seed.Start.AddDays(8), cancelled.Token));
        Assert.Equal(2, await verify.RankingSnapshots.CountAsync());
    }

    [Fact]
    public async Task SnapshotPolling_YearsOfHistory_FillsHistoricalGapAndUsesTwoIdleQueries()
    {
        var seed = await SeedAsync(new DateTimeOffset(2023, 1, 2, 0, 0, 0, TimeSpan.Zero));
        var now = new DateTimeOffset(2026, 1, 6, 12, 0, 0, TimeSpan.Zero);
        int expectedCount;
        await using (var context = database.CreateContext())
        {
            var earliest = (await new RankingRepository(context).GetEarliestLikeActivityUtcAsync())!.Value;
            var snapshots = new List<RankingSnapshot>();
            foreach (var period in Enum.GetValues<RankingPeriod>())
            {
                var currentStart = RankingPeriodBounds.Current(period, now).StartUtc;
                for (var start = RankingPeriodBounds.Current(period, earliest).StartUtc; start < currentStart;
                     start = RankingPeriodBounds.Next(period, start))
                    snapshots.Add(new RankingSnapshot
                    {
                        Period = period, PeriodStartUtc = start, PeriodEndUtc = RankingPeriodBounds.Next(period, start),
                        CreatedAtUtc = now, Entries = [new RankingSnapshotEntry { ImageId = seed.WinnerId, Rank = 1, LikeDelta = 1 }]
                    });
            }
            expectedCount = snapshots.Count;
            Assert.True(expectedCount > 1000);
            context.RankingSnapshots.AddRange(snapshots);
            await context.SaveChangesAsync();
            await context.RankingSnapshots.Where(snapshot => snapshot.Period == RankingPeriod.Daily && snapshot.PeriodStartUtc == seed.Start)
                .ExecuteDeleteAsync();
        }
        var capture = new QueryCapture();
        await using var services = new ServiceCollection()
            .AddScoped(_ => database.CreateContext(capture))
            .AddScoped<IRepositoryManager, RepositoryManager>()
            .BuildServiceProvider();
        using var worker = new RankingSnapshotPollingService(services.GetRequiredService<IServiceScopeFactory>(),
            new FixedClock(now), NullLogger<RankingSnapshotPollingService>.Instance);
        await worker.CreateMissingSnapshotsAsync(CancellationToken.None);
        Assert.Equal(3, capture.Reads.Count);
        Assert.Empty(capture.Entities);
        await using (var verify = database.CreateContext())
        {
            Assert.Equal(expectedCount, await verify.RankingSnapshots.CountAsync());
            var repaired = await verify.RankingSnapshots.Include(snapshot => snapshot.Entries)
                .SingleAsync(snapshot => snapshot.Period == RankingPeriod.Daily && snapshot.PeriodStartUtc == seed.Start);
            Assert.Equal(44, repaired.Entries.Count);
        }
        capture.Clear();
        await worker.CreateMissingSnapshotsAsync(CancellationToken.None);
        Assert.Equal(2, capture.Reads.Count);
        Assert.All(capture.Reads, sql => Assert.DoesNotContain("RankingSnapshotEntries", sql));
        Assert.Empty(capture.Entities);
    }

    [Fact]
    public async Task LiveRankings_ImageHiddenBetweenQueries_ReturnConsistentCountAndCards()
    {
        var seed = await SeedAsync();
        var capture = new QueryCapture(async () =>
        {
            await using var writer = database.CreateContext();
            await writer.Images.Where(image => image.Id == seed.WinnerId)
                .ExecuteUpdateAsync(update => update.SetProperty(image => image.DeletedAtUtc, seed.Start.AddHours(2)));
        });
        await using var context = database.CreateContext(capture);
        var repository = new RankingRepository(context);
        var page = await repository.GetLivePageAsync(seed.Start, seed.Start.AddDays(1), 1, 1, default, null);
        Assert.Equal(44, page.Total);
        Assert.Equal(seed.WinnerId, Assert.Single(page.Entries).Image.Id);
        Assert.Null(page.Entries[0].Image.DeletedAtUtc);
        var next = await repository.GetLivePageAsync(seed.Start, seed.Start.AddDays(1), 1, 1, default, null);
        Assert.Equal(43, next.Total);
        Assert.Equal(seed.HighTieId, Assert.Single(next.Entries).Image.Id);
    }

    [Theory]
    [InlineData(RankingPeriod.Weekly)]
    [InlineData(RankingPeriod.Monthly)]
    public async Task LiveRankings_CalendarPeriods_UseTheirCompleteUtcInterval(RankingPeriod period)
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        var handler = new GetRankingsHandler(new RepositoryManager(context), new Viewer(seed.OwnerId),
            new FixedClock(seed.Start.AddHours(12)));
        var result = await handler.Handle(new GetRankingsCommand(period, null, 1, 20), CancellationToken.None);
        var bounds = RankingPeriodBounds.Current(period, seed.Start);
        Assert.Equal(bounds.StartUtc, result.PeriodStartUtc);
        Assert.Equal(bounds.EndUtc, result.PeriodEndUtc);
        Assert.Equal(44, result.Total);
        // Tomorrow's activity is inside both periods; yesterday's activity is inside the month.
        Assert.Equal(period == RankingPeriod.Monthly ? 5 : 4, result.Entries[0].LikeDelta);
        Assert.Equal(seed.WinnerId, result.Entries[0].Image.Id);
    }

    private async Task<Seed> SeedAsync(DateTimeOffset? periodStart = null)
    {
        var start = periodStart ?? new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        await using var context = database.CreateContext();
        await context.RankingSnapshots.ExecuteDeleteAsync();
        await context.ImageLikeActivities.ExecuteDeleteAsync();
        await context.Images.ExecuteDeleteAsync();
        await context.Tags.ExecuteDeleteAsync();
        var owner = new AppUser { Login = "rank-owner", NormalizedLogin = Guid.NewGuid().ToString("N"),
            PasswordHash = "unused", CreatedAtUtc = start };
        context.AppUsers.Add(owner);
        await context.SaveChangesAsync();
        var images = Enumerable.Range(0, 49).Select(index => new UserMadeImage
        {
            Source = ImageSource.UserUpload, UploadedById = owner.Id,
            UploadedAtUtc = index == 0 ? start.AddDays(-20) : index == 1 ? start.AddDays(-2) : start.AddDays(-1),
            Visibility = index == 4 ? ImageVisibility.Private : ImageVisibility.Gallery,
            ModerationStatus = index == 5 ? ModerationStatus.Pending : ModerationStatus.Approved,
            DeletedAtUtc = index == 6 ? start : null, StorageKey = $"uploads/{Guid.NewGuid():N}.jpg",
            ContentType = "image/jpeg", Width = 100, Height = 80,
            AiUsage = index == 0 || index == 1 || index == 2 ? AiUsageClassification.HumanMade : AiUsageClassification.AiGenerated
        }).ToArray();
        images[0].Tags = new[] { TagModerationStatus.Approved, TagModerationStatus.Pending }
            .Select(status => new ImageTag { Name = status.ToString().ToLowerInvariant(),
                NormalizedName = status.ToString().ToLowerInvariant(), ModerationStatus = status, CreatedAtUtc = start }).ToList();
        images[0].Likes = [new ImageLike { UserId = owner.Id, CreatedAtUtc = start }];
        images[0].Comments = [new Comment { AuthorId = owner.Id, Content = "visible", CreatedAtUtc = start },
            new Comment { AuthorId = owner.Id, Content = "deleted", CreatedAtUtc = start, DeletedAtUtc = start }];
        context.Images.AddRange(images);
        await context.SaveChangesAsync();
        for (var index = 0; index < images.Length; index++)
        {
            var likes = index == 0 ? 4 : index <= 3 ? 2 : index <= 6 ? 10 : 1;
            for (var count = 0; count < likes; count++)
                context.ImageLikeActivities.Add(Activity(images[index].Id, owner.Id, start.AddHours(1), LikeActivityType.Like));
            if (index == 0 || index == 7 || index == 8)
                context.ImageLikeActivities.Add(Activity(images[index].Id, owner.Id, start.AddHours(2), LikeActivityType.Unlike));
            if (index == 8)
                context.ImageLikeActivities.Add(Activity(images[index].Id, owner.Id, start.AddHours(3), LikeActivityType.Unlike));
        }
        // The UTC interval is inclusive at its start and exclusive at its end.
        context.ImageLikeActivities.Add(Activity(images[0].Id, owner.Id, start.AddTicks(-1), LikeActivityType.Like));
        context.ImageLikeActivities.Add(Activity(images[0].Id, owner.Id, start.AddDays(1), LikeActivityType.Like));
        await context.SaveChangesAsync();
        return new Seed(start, owner.Id, images[0].Id, images[3].Id, images[2].Id, images[1].Id);
    }

    private static ImageLikeActivity Activity(int imageId, int userId, DateTimeOffset now, LikeActivityType type)
        => new() { ImageId = imageId, UserId = userId, OccurredAtUtc = now, Type = type };

    private sealed record Seed(DateTimeOffset Start, int OwnerId, int WinnerId, int HighTieId, int LowTieId, int OlderTieId);
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class Viewer(int id) : IUserContext
    {
        public int? UserId => id;
        public string? Login => "rank-owner";
        public bool IsInRole(string role) => false;
        public void RequireAuthenticated() { }
    }
}
