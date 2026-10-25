using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Configuration;
using Npgsql;
using Pgvector;
using Repository;
using Service;
using Shared.DataTransferObjects;

namespace GallerySiteIntegrationTests;

public sealed class ForMeFeedTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private static readonly ForMeFilters All = new([], []);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Create_MixedInterests_InterleavesNeighborsWithoutReturningLikesOrIneligibleImages()
    {
        var seed = await SeedAsync();
        var capture = new QueryCapture();
        await using var context = database.CreateContext(capture);
        var repository = new ForMeFeedRepository(context, seed.Clock, NullLogger<ForMeFeedRepository>.Instance, Options.Create(new ForMeFeedOptions()));
        var id = await repository.CreateAsync(seed.UserId, 20, All);
        var page = await repository.ReadAsync(id, new ImageViewer(seed.UserId, true), 0);

        Assert.Equal("Personalized", page.Feed.Mode);
        Assert.Equal(seed.Images.Skip(2).Take(4).Select(image => image.Id), page.Images.Select(image => image.Id));
        Assert.All(page.Images, image => Assert.False(image.IsLikedByCurrentUser));
        Assert.False(page.HasMore);
        Assert.Contains(capture.Reads, sql => sql.Contains("ForMeAnnNeighbors") && !sql.Contains("+ 0.0") && sql.Contains("LIMIT"));
        Assert.DoesNotContain(capture.Entities, entity => entity is AppImage or ImageLike);
    }

    [Fact]
    public async Task Read_LikesAndModerationChange_SuppressesRowsWithoutReorderingOrDuplicating()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        var repository = new ForMeFeedRepository(context, seed.Clock, NullLogger<ForMeFeedRepository>.Instance, Options.Create(new ForMeFeedOptions()));
        var id = await repository.CreateAsync(seed.UserId, 1, All);
        var first = await repository.ReadAsync(id, new(seed.UserId, false), 0);
        Assert.Equal(seed.Images[2].Id, Assert.Single(first.Images).Id);
        await using (var mutation = database.CreateContext())
        {
            mutation.ImageLikes.Add(new ImageLike
                { ImageId = seed.Images[3].Id, UserId = seed.UserId, CreatedAtUtc = seed.Clock.GetUtcNow() });
            await mutation.Images.Where(image => image.Id == seed.Images[4].Id)
                .ExecuteUpdateAsync(update => update.SetProperty(image => image.Visibility, ImageVisibility.Private));
            await mutation.SaveChangesAsync();
        }
        var second = await repository.ReadAsync(id, new(seed.UserId, true), first.LastRank);
        Assert.Equal(seed.Images[5].Id, Assert.Single(second.Images).Id);
        Assert.Equal(4, second.Feed.Total);
        Assert.False(second.HasMore);
        Assert.Equal(first.Feed.CreatedAtUtc, second.Feed.CreatedAtUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_NoUsableLikes_UsesCurrentLifetimePopularityIncludingZeroLikeImages(bool likesWithoutVectors)
    {
        var seed = await SeedAsync(personalized: likesWithoutVectors);
        await using var context = database.CreateContext();
        if (likesWithoutVectors)
        {
            var likedIds = seed.Images.Take(2).Select(image => image.Id).ToArray();
            await context.Images.Where(image => Enumerable.Contains(likedIds, image.Id))
                .ExecuteUpdateAsync(update => update.SetProperty(image => image.Embedding, (Vector?)null));
        }
        context.ImageLikes.AddRange(
            new ImageLike { ImageId = seed.Images[2].Id, UserId = seed.OtherId, CreatedAtUtc = seed.Clock.GetUtcNow().AddYears(-1) },
            new ImageLike { ImageId = seed.Images[2].Id, UserId = seed.ThirdId, CreatedAtUtc = seed.Clock.GetUtcNow() },
            new ImageLike { ImageId = seed.Images[3].Id, UserId = seed.OtherId, CreatedAtUtc = seed.Clock.GetUtcNow() });
        await context.SaveChangesAsync();
        var repository = new ForMeFeedRepository(context, seed.Clock, NullLogger<ForMeFeedRepository>.Instance, Options.Create(new ForMeFeedOptions()));
        var id = await repository.CreateAsync(seed.UserId, 20, All);
        var page = await repository.ReadAsync(id, new(seed.UserId, false), 0);

        Assert.Equal("Fallback", page.Feed.Mode);
        Assert.Equal(new[] { seed.Images[2].Id, seed.Images[3].Id }, page.Images.Take(2).Select(image => image.Id));
        Assert.Equal(likesWithoutVectors ? 4 : 6, page.Images.Count);
        Assert.Equal(seed.Images[5].Id, page.Images[2].Id);
    }

    [Fact]
    public async Task Create_ResultFilters_DoNotFilterSeedsAndApplyToBothModes()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        var tag = new ImageTag { Name = "landscape", NormalizedName = "landscape", ModerationStatus = TagModerationStatus.Approved };
        var excluded = await context.Images.SingleAsync(image => image.Id == seed.Images[3].Id);
        excluded.Tags.Add(tag);
        await context.Images.Where(image => image.Id == seed.Images[0].Id || image.Id == seed.Images[1].Id)
            .ExecuteUpdateAsync(update => update.SetProperty(image => image.AiUsage, AiUsageClassification.AiGenerated));
        await context.SaveChangesAsync();
        var filters = new ForMeFilters([(int)AiUsageClassification.HumanMade], ["landscape"]);
        var repository = new ForMeFeedRepository(context, seed.Clock, NullLogger<ForMeFeedRepository>.Instance, Options.Create(new ForMeFeedOptions()));
        var id = await repository.CreateAsync(seed.UserId, 20, filters);
        var personalized = await repository.ReadAsync(id, new(seed.UserId, true), 0);
        Assert.Equal("Personalized", personalized.Feed.Mode);
        Assert.DoesNotContain(personalized.Images, image => image.Id == excluded.Id);
        await context.ImageLikes.Where(like => like.UserId == seed.UserId).ExecuteDeleteAsync();
        var fallbackId = await repository.CreateAsync(seed.UserId, 20, filters);
        var fallback = await repository.ReadAsync(fallbackId, new(seed.UserId, false), 0);
        Assert.Equal("Fallback", fallback.Feed.Mode);
        Assert.DoesNotContain(fallback.Images, image => image.Id == excluded.Id || image.AiUsage != AiUsageClassification.HumanMade);
    }

    [Fact]
    public async Task Create_LatestHundredSeeds_ExcludesOlderInterestAndEveryCurrentLike()
    {
        var seed = await SeedAsync(personalized: false);
        await using var context = database.CreateContext();
        await context.Images.ExecuteDeleteAsync();
        var now = seed.Clock.GetUtcNow();
        var liked = Enumerable.Range(0, 101).Select(index => Image(seed.UserId, now, index == 0 ? 1000 : 0)).ToArray();
        var candidates = Enumerable.Range(1, 500).Select(index => Image(seed.UserId, now, index / 100f)).ToArray();
        var oldInterest = Image(seed.UserId, now, 1001);
        context.Images.AddRange(liked.Concat(candidates).Append(oldInterest));
        await context.SaveChangesAsync();
        context.ImageLikes.AddRange(liked.Select((image, index) => new ImageLike
            { ImageId = image.Id, UserId = seed.UserId, CreatedAtUtc = now.AddMinutes(index) }));
        await context.SaveChangesAsync();
        var repository = new ForMeFeedRepository(context, seed.Clock, NullLogger<ForMeFeedRepository>.Instance, Options.Create(new ForMeFeedOptions()));
        var id = await repository.CreateAsync(seed.UserId, 50, All);
        var stored = await context.ForMeFeedEntries.Where(entry => entry.FeedId == id).Select(entry => entry.ImageId).ToListAsync();
        Assert.Equal(500, stored.Count);
        Assert.DoesNotContain(oldInterest.Id, stored);
        Assert.Empty(stored.Intersect(liked.Select(image => image.Id)));
    }

    [Fact]
    public async Task Read_SharedDatabaseAndKeyRing_PreservesFeedAcrossApplicationInstancesAndExpiry()
    {
        var seed = await SeedAsync();
        await using var firstApp = new ApiFactory(database, seed.Clock);
        using var first = firstApp.Client();
        first.DefaultRequestHeaders.Authorization = new("Bearer", seed.Token);
        var initial = (await first.GetFromJsonAsync<ForMeFeedDto>("/api/images/for-me?pageSize=1", JsonOptions))!;
        Assert.NotNull(initial.NextCursor);
        await using var secondApp = new ApiFactory(database, seed.Clock);
        using var second = secondApp.Client();
        second.DefaultRequestHeaders.Authorization = new("Bearer", seed.Token);
        var continuation = (await second.GetFromJsonAsync<ForMeFeedDto>("/api/images/for-me?cursor=" + Uri.EscapeDataString(initial.NextCursor), JsonOptions))!;
        Assert.Equal(initial.GeneratedAtUtc, continuation.GeneratedAtUtc);
        Assert.NotEqual(initial.Images[0].Id, continuation.Images[0].Id);
        seed.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(HttpStatusCode.Conflict,
            (await second.GetAsync("/api/images/for-me?cursor=" + Uri.EscapeDataString(initial.NextCursor))).StatusCode);
    }

    [Fact]
    public async Task Api_AuthenticationValidationAndCacheContract_AreEnforced()
    {
        var seed = await SeedAsync();
        await using var app = new ApiFactory(database, seed.Clock);
        using var client = app.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/images/for-me")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", seed.Token);
        foreach (var query in new[] { "cursor=", "cursor=bad", "pageSize=bad", "aiUsage=bad", "cursor=x&pageSize=", "cursor=x&excludedTags=x" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/images/for-me?" + query)).StatusCode);
        await using (var context = database.CreateContext()) Assert.Empty(await context.ForMeFeeds.ToListAsync());
        using var response = await client.GetAsync("/api/images/for-me?pageSize=1");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl!.Private);
        Assert.True(response.Headers.CacheControl.NoStore);
        Assert.Contains("Authorization", response.Headers.Vary);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("embedding", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storageKey", body);
        var dto = (await response.Content.ReadFromJsonAsync<ForMeFeedDto>(JsonOptions))!;
        var protector = new ForMeFeedCursorProtector(DataProtectionProvider.Create(new DirectoryInfo(database.KeyPath),
            builder => builder.SetApplicationName("Lilgallery")));
        var decoded = protector.Unprotect(dto.NextCursor!);
        var foreign = protector.Protect(decoded with { UserId = seed.OtherId });
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/images/for-me?cursor=" + Uri.EscapeDataString(foreign))).StatusCode);
    }

    [Fact]
    public async Task Api_ServerGenerationDeadline_Returns503ProblemDetailsWithoutPublishing()
    {
        var seed = await SeedAsync();
        var clock = new ImmediateGenerationDeadlineClock(seed.Clock);
        await using var app = new ApiFactory(database, clock);
        using var client = app.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/images/for-me")).StatusCode);
        Assert.Equal(0, clock.DeadlinesFired);
        client.DefaultRequestHeaders.Authorization = new("Bearer", seed.Token);

        using var response = await client.GetAsync("/api/images/for-me");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>())!;
        Assert.Equal(503, problem.Status);
        Assert.Equal("Service Unavailable", problem.Title);
        Assert.Equal("Recommendation generation timed out. Retry later.", problem.Detail);
        Assert.True(problem.Extensions.TryGetValue("traceId", out var traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId!.ToString()));
        Assert.Equal(1, clock.DeadlinesFired);
        await using var context = database.CreateContext();
        Assert.Empty(await context.ForMeFeeds.ToListAsync());
        Assert.Empty(await context.ForMeFeedEntries.ToListAsync());
    }

    [Fact]
    public async Task Create_BothGenerationSlotsBusy_RejectsWithoutPublishing()
    {
        var seed = await SeedAsync();
        await using var holder = database.CreateContext();
        await using var transaction = await holder.Database.BeginTransactionAsync();
        for (var slot = 0; slot < 2; slot++)
            Assert.True(await holder.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({ForMeFeedRepository.GenerationLockNamespace}, {slot}) AS \"Value\"").SingleAsync());
        await using var context = database.CreateContext();
        await Assert.ThrowsAsync<ForMeFeedLimitException>(() => new ForMeFeedRepository(context, seed.Clock, NullLogger<ForMeFeedRepository>.Instance, Options.Create(new ForMeFeedOptions())).CreateAsync(seed.UserId, 20, All));
        Assert.Empty(await context.ForMeFeeds.ToListAsync());
    }

    [Fact]
    public async Task Create_ConcurrentPublicationsAtCapacity_WaiterSeesCommittedCount()
    {
        var seed = await SeedAsync();
        await using (var setup = database.CreateContext())
        {
            setup.ForMeFeeds.AddRange(Enumerable.Range(0, 99).Select(_ => Header(seed.UserId, seed.Clock.GetUtcNow())));
            await setup.SaveChangesAsync();
        }
        var firstLocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAttempt = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var firstContext = database.CreateContext(new PublicationGate(firstLocked, release));
        await using var secondContext = database.CreateContext(new PublicationGate(secondAttempt));
        var firstTask = new ForMeFeedRepository(firstContext, seed.Clock, NullLogger<ForMeFeedRepository>.Instance, Options.Create(new ForMeFeedOptions())).CreateAsync(seed.UserId, 20, All);
        await firstLocked.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var secondTask = new ForMeFeedRepository(secondContext, seed.Clock, NullLogger<ForMeFeedRepository>.Instance, Options.Create(new ForMeFeedOptions())).CreateAsync(seed.UserId, 20, All);
        var pid = await secondAttempt.Task.WaitAsync(TimeSpan.FromSeconds(20));
        try { await WaitForDatabaseLockAsync(pid); }
        finally { release.TrySetResult(); }
        await firstTask;
        await Assert.ThrowsAsync<ForMeFeedLimitException>(() => secondTask);
        await using var check = database.CreateContext();
        Assert.Equal(100, await check.ForMeFeeds.CountAsync(feed => feed.UserId == seed.UserId));
    }

    [Fact]
    public async Task Delete_ImageOrExpiredFeed_CascadesEntriesAndRetainsOriginalCountUntilExpiry()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        var repository = new ForMeFeedRepository(context, seed.Clock, NullLogger<ForMeFeedRepository>.Instance, Options.Create(new ForMeFeedOptions()));
        var id = await repository.CreateAsync(seed.UserId, 20, All);
        await context.Images.Where(image => image.Id == seed.Images[2].Id).ExecuteDeleteAsync();
        var page = await repository.ReadAsync(id, new(seed.UserId, false), 0);
        Assert.Equal(4, page.Feed.Total);
        Assert.Equal(3, page.Images.Count);
        Assert.DoesNotContain(page.Images, image => image.Id == seed.Images[2].Id);
        seed.Clock.Advance(TimeSpan.FromHours(1));
        await Assert.ThrowsAsync<Base409ConflictException>(() => repository.ReadAsync(id, new(seed.UserId, false), 0));
        Assert.Equal(1, await repository.DeleteExpiredAsync());
        Assert.Empty(await context.ForMeFeedEntries.ToListAsync());
    }

    [Fact]
    public async Task Create_UnembeddedCandidatesAndAllLikedGallery_ReturnsFallbackThenEmptyFeed()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        var candidateIds = seed.Images.Skip(2).Take(4).Select(image => image.Id).ToArray();
        await context.Images.Where(image => Enumerable.Contains(candidateIds, image.Id))
            .ExecuteUpdateAsync(update => update.SetProperty(image => image.Embedding, (Vector?)null));
        var repository = new ForMeFeedRepository(context, seed.Clock, NullLogger<ForMeFeedRepository>.Instance, Options.Create(new ForMeFeedOptions()));
        var id = await repository.CreateAsync(seed.UserId, 20, All);
        var fallback = await repository.ReadAsync(id, new(seed.UserId, false), 0);
        Assert.Equal("Fallback", fallback.Feed.Mode);
        Assert.Equal(4, fallback.Images.Count);
        context.ImageLikes.AddRange(candidateIds.Select(imageId => new ImageLike
            { UserId = seed.UserId, ImageId = imageId, CreatedAtUtc = seed.Clock.GetUtcNow() }));
        await context.SaveChangesAsync();
        var emptyId = await repository.CreateAsync(seed.UserId, 20, All);
        var empty = await repository.ReadAsync(emptyId, new(seed.UserId, false), 0);
        Assert.Equal("Fallback", empty.Feed.Mode);
        Assert.Equal(0, empty.Feed.Total);
        Assert.Empty(empty.Images);
        await context.ImageLikes.Where(like => like.UserId == seed.UserId && like.ImageId == seed.Images[0].Id).ExecuteDeleteAsync();
        var refreshedId = await repository.CreateAsync(seed.UserId, 20, All);
        var refreshed = await repository.ReadAsync(refreshedId, new(seed.UserId, false), 0);
        Assert.Equal("Personalized", refreshed.Feed.Mode);
        Assert.Equal(seed.Images[0].Id, Assert.Single(refreshed.Images).Id);
    }

    [Fact]
    public async Task Api_RequestBudgets_SeparateCreationFromContinuation()
    {
        var seed = await SeedAsync();
        await using var app = new ApiFactory(database, seed.Clock);
        using var client = app.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", seed.Token);
        var initial = (await client.GetFromJsonAsync<ForMeFeedDto>("/api/images/for-me?pageSize=1", JsonOptions))!;
        for (var index = 1; index < 10; index++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/images/for-me")).StatusCode);
        using var limited = await client.GetAsync("/api/images/for-me");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.Equal(429, (await limited.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>())!.Status);
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync("/api/images/for-me?cursor=" + Uri.EscapeDataString(initial.NextCursor!))).StatusCode);
    }

    [Fact]
    public async Task DeleteExpired_UsesBoundedBatchesAndPreservesLiveFeeds()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        context.ForMeFeeds.AddRange(Enumerable.Range(0, 1001).Select(_ => Header(seed.UserId, seed.Clock.GetUtcNow().AddHours(-2))));
        context.ForMeFeeds.Add(Header(seed.UserId, seed.Clock.GetUtcNow()));
        await context.SaveChangesAsync();
        var repository = new ForMeFeedRepository(context, seed.Clock, NullLogger<ForMeFeedRepository>.Instance, Options.Create(new ForMeFeedOptions()));
        Assert.Equal(1000, await repository.DeleteExpiredAsync());
        Assert.Equal(1, await repository.DeleteExpiredAsync());
        Assert.Equal(1, await context.ForMeFeeds.CountAsync());
    }

    [Fact]
    public async Task Create_HardDeleteDuringPublication_WaitsForKeyLocksThenCascadesSafely()
    {
        var seed = await SeedAsync();
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var publisher = database.CreateContext(new ImageKeyGate(locked, release));
        var creation = new ForMeFeedRepository(publisher, seed.Clock, NullLogger<ForMeFeedRepository>.Instance, Options.Create(new ForMeFeedOptions())).CreateAsync(seed.UserId, 20, All);
        await locked.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await using var deleting = database.CreateContext();
        await deleting.Database.OpenConnectionAsync();
        var pid = ((NpgsqlConnection)deleting.Database.GetDbConnection()).ProcessID;
        var deletion = deleting.Images.Where(image => image.Id == seed.Images[2].Id).ExecuteDeleteAsync();
        try { await WaitForDatabaseLockAsync(pid); }
        finally { release.TrySetResult(); }
        var id = await creation;
        Assert.Equal(1, await deletion);
        var page = await new ForMeFeedRepository(publisher, seed.Clock, NullLogger<ForMeFeedRepository>.Instance, Options.Create(new ForMeFeedOptions())).ReadAsync(id, new(seed.UserId, false), 0);
        Assert.Equal(4, page.Feed.Total);
        Assert.Equal(3, page.Images.Count);
    }

    [Fact]
    public async Task Create_CanceledPublication_RollsBackHeaderAndEntries()
    {
        var seed = await SeedAsync();
        using var cancellation = new CancellationTokenSource();
        await using var context = database.CreateContext(new CancelInsert(cancellation));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ForMeFeedRepository(context, seed.Clock, NullLogger<ForMeFeedRepository>.Instance, Options.Create(new ForMeFeedOptions())).CreateAsync(seed.UserId, 20, All, cancellation.Token));
        await using var check = database.CreateContext();
        Assert.Empty(await check.ForMeFeeds.ToListAsync());
        Assert.Empty(await check.ForMeFeedEntries.ToListAsync());
    }

    private async Task WaitForDatabaseLockAsync(int pid)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var context = database.CreateContext();
        while (!await context.Database.SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = {pid} AND wait_event_type = 'Lock') AS \"Value\"").SingleAsync(timeout.Token))
            await Task.Delay(10, timeout.Token);
    }

    private async Task<Seed> SeedAsync(bool personalized = true)
    {
        var clock = new TestClock();
        await using var context = database.CreateContext();
        await context.Images.ExecuteDeleteAsync();
        await context.ForMeFeeds.ExecuteDeleteAsync();
        await context.AppUsers.ExecuteDeleteAsync();
        var auth = database.Authentication(context, clock);
        var account = await auth.RegisterAsync(new("forme" + Guid.NewGuid().ToString("N"), "integration-password"));
        var other = User("other");
        var third = User("third");
        context.AppUsers.AddRange(other, third);
        await context.SaveChangesAsync();
        var id = account.Response.User.Id;
        var now = clock.GetUtcNow();
        var images = new[] { 0f, 1000f, 1f, 999f, 2f, 998f, 0.1f, 999.9f }
            .Select((value, index) => Image(id, now.AddMinutes(index), value)).ToArray();
        images[6].Visibility = ImageVisibility.Private;
        images[7].ModerationStatus = ModerationStatus.Pending;
        context.Images.AddRange(images);
        await context.SaveChangesAsync();
        if (personalized)
        {
            context.ImageLikes.AddRange(images.Take(2).Select((image, index) => new ImageLike
                { ImageId = image.Id, UserId = id, CreatedAtUtc = now.AddMinutes(-index) }));
            await context.SaveChangesAsync();
        }
        return new Seed(id, other.Id, third.Id, account.Response.AccessToken, clock, images);
    }

    private static AppUser User(string name) => new()
    {
        Login = name + Guid.NewGuid().ToString("N"), NormalizedLogin = Guid.NewGuid().ToString("N"),
        PasswordHash = "unused", CreatedAtUtc = DateTimeOffset.UtcNow
    };

    private static UserMadeImage Image(int userId, DateTimeOffset at, float value)
    {
        var values = new float[1024];
        values[0] = value;
        return new UserMadeImage
        {
            UploadedById = userId, UploadedAtUtc = at, Source = ImageSource.UserUpload,
            Visibility = ImageVisibility.Gallery, ModerationStatus = ModerationStatus.Approved,
            AiUsage = AiUsageClassification.HumanMade, Embedding = new Vector(values), EmbeddingStatus = EmbeddingStatus.Ready,
            StorageKey = Guid.NewGuid().ToString("N"), ContentType = "image/jpeg", Width = 1, Height = 1
        };
    }

    private static ForMeFeed Header(int userId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, PageSize = 20, CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(1)
    };

    private sealed record Seed(int UserId, int OtherId, int ThirdId, string Token, TestClock Clock, UserMadeImage[] Images);

    private sealed class ImmediateGenerationDeadlineClock(TimeProvider clock) : TimeProvider
    {
        public int DeadlinesFired { get; private set; }
        public override DateTimeOffset GetUtcNow() => clock.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime != TimeSpan.FromSeconds(10) || period != Timeout.InfiniteTimeSpan)
                return System.CreateTimer(callback, state, dueTime, period);
            DeadlinesFired++;
            callback(state);
            return new NoOpTimer();
        }

        private sealed class NoOpTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class ImageKeyGate(TaskCompletionSource locked, TaskCompletionSource release) : DbCommandInterceptor
    {
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR KEY SHARE"))
            {
                locked.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class CancelInsert(CancellationTokenSource cancellation) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"ForMeFeeds\""))
            {
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class PublicationGate : DbCommandInterceptor
    {
        private readonly TaskCompletionSource? _locked;
        private readonly TaskCompletionSource? _release;
        private readonly TaskCompletionSource<int>? _attempt;

        public PublicationGate(TaskCompletionSource locked, TaskCompletionSource release)
            => (_locked, _release) = (locked, release);
        public PublicationGate(TaskCompletionSource<int> attempt) => _attempt = attempt;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"AppUsers\"") && command.CommandText.Contains("FOR UPDATE"))
                _attempt?.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (_locked is not null && command.CommandText.Contains("FROM \"AppUsers\"") && command.CommandText.Contains("FOR UPDATE"))
            {
                _locked.TrySetResult();
                await _release!.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
