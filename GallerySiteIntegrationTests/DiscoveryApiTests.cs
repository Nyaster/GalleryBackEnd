using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Entities.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Repository;
using Shared.DataTransferObjects;

namespace GallerySiteIntegrationTests;

public sealed class DiscoveryApiTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    [Theory]
    [InlineData("most-liked")]
    [InlineData("new-uploads")]
    [InlineData("most-commented")]
    public async Task GetDiscovery_MissingOrExpiredSession_ReturnsUnauthorized(string collection)
    {
        var clock = new TestClock();
        await using var app = new ApiFactory(database, clock);
        using var client = app.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/discovery/" + collection)).StatusCode);

        await using var context = database.CreateContext();
        var account = await database.Authentication(context, clock).RegisterAsync(
            new("expired" + Guid.NewGuid().ToString("N"), "integration-password"));
        await context.AppUsers.Where(user => user.Id == account.Response.User.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.AuthenticationVersion, 1));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", account.Response.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/discovery/" + collection)).StatusCode);
    }

    [Fact]
    public async Task GetDiscovery_InvalidCollectionDateFilterOrPaging_ReturnsBadRequest()
    {
        var seed = await SeedAsync();
        await using var app = new ApiFactory(database, seed.Clock);
        using var client = app.Client();
        await SignInAsync(client, seed);
        foreach (var url in new[]
                 {
                     "/api/discovery/popular", "/api/discovery/MostLiked", "/api/discovery/most-liked?date=",
                     "/api/discovery/most-liked?date=2026-02-29", "/api/discovery/most-liked?date=2026-1-01",
                     "/api/discovery/most-liked?date=2026-10-04T00:00:00Z",
                     "/api/discovery/most-liked?date=" + seed.Start.AddDays(1).ToString("yyyy-MM-dd"),
                     "/api/discovery/most-liked?aiUsage=invalid", "/api/discovery/most-liked?aiUsage=100",
                     "/api/discovery/most-liked?page=invalid"
                 })
        {
            using var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.Equal(400, problem!.Status);
            Assert.True(problem.Extensions.ContainsKey("traceId"));
        }
    }

    [Theory]
    [InlineData("most-liked")]
    [InlineData("most-commented")]
    public async Task GetDiscovery_Popularity_UsesDailyBoundariesLifetimeCountsAndStableOrder(string collection)
    {
        var seed = await SeedAsync();
        await using var app = new ApiFactory(database, seed.Clock);
        using var client = app.Client();
        await SignInAsync(client, seed);

        using var response = await client.GetAsync($"/api/discovery/{collection}?pageSize=2");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.Private);
        var result = (await response.Content.ReadFromJsonAsync<PageableDiscoveryDto>(JsonOptions))!;
        Assert.Equal(8, result.GalleryTotal);
        Assert.Equal(5, result.Total);
        Assert.Equal(new[] { seed.Winner.Id, seed.Scraped.Id }, result.Entries.Select(entry => entry.Image.Id));
        var winner = result.Entries[0];
        Assert.Equal(collection == "most-liked" ? 2 : null, winner.PeriodLikeCount);
        Assert.Equal(collection == "most-commented" ? 2 : null, winner.PeriodCommentCount);
        Assert.Equal(4, winner.Image.LikeCount);
        Assert.Equal(4, winner.Image.CommentCount);
        Assert.True(winner.Image.IsLikedByCurrentUser);
        Assert.Equal(seed.Viewer.Login, winner.Image.UploadedBy);
        Assert.Equal(new[] { "approved" }, winner.Image.Tags);
        Assert.Null(winner.Image.PendingTags);
        Assert.Null(winner.Image.HiddenAtUtc);
        Assert.True(winner.Image.UploadedAtUtc < seed.Start);

        var next = await ReadAsync(client, $"/api/discovery/{collection}?page=2&pageSize=2");
        Assert.Equal(new[] { seed.NewerTieHigh.Id, seed.NewerTieLow.Id }, next.Entries.Select(entry => entry.Image.Id));
        var last = await ReadAsync(client, $"/api/discovery/{collection}?page=3&pageSize=2");
        Assert.Equal(seed.OlderTie.Id, Assert.Single(last.Entries).Image.Id);
        Assert.False(Assert.Single(last.Entries).Image.IsLikedByCurrentUser);
        var beyond = await ReadAsync(client, $"/api/discovery/{collection}?page=2147483647&pageSize=50");
        Assert.Equal(5, beyond.Total);
        Assert.Equal(8, beyond.GalleryTotal);
        Assert.Empty(beyond.Entries);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var envelope = json.RootElement;
        Assert.Equal("Daily", envelope.GetProperty("period").GetString());
        Assert.Equal("UTC", envelope.GetProperty("timeZone").GetString());
        Assert.Equal(seed.Start.ToString("yyyy-MM-dd"), envelope.GetProperty("date").GetString());
        foreach (var name in new[] { "periodStartUtc", "periodEndUtc", "generatedAtUtc" })
            Assert.EndsWith("Z", envelope.GetProperty(name).GetString());
        Assert.Equal(seed.Start.UtcDateTime, result.PeriodStartUtc);
        Assert.Equal(seed.Start.AddDays(1).UtcDateTime, result.PeriodEndUtc);
        Assert.Equal(seed.Clock.GetUtcNow().UtcDateTime, result.GeneratedAtUtc);
        var card = envelope.GetProperty("entries")[0];
        Assert.False(card.TryGetProperty(collection == "most-liked" ? "periodCommentCount" : "periodLikeCount", out _));
        Assert.False(card.GetProperty("image").TryGetProperty("storageKey", out _));
        Assert.DoesNotContain("pending-secret", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetDiscovery_NewUploads_OnlyIncludesCommunityUploadsDuringDay()
    {
        var seed = await SeedAsync();
        await using var app = new ApiFactory(database, seed.Clock);
        using var client = app.Client();
        await SignInAsync(client, seed);
        var result = await ReadAsync(client, "/api/discovery/new-uploads?page=0&pageSize=500");

        Assert.Equal(1, result.Page);
        Assert.Equal(50, result.PageSize);
        Assert.Equal(8, result.GalleryTotal);
        Assert.Equal(3, result.Total);
        Assert.Equal(new[] { seed.NewerTieHigh.Id, seed.NewerTieLow.Id, seed.StartUpload.Id },
            result.Entries.Select(entry => entry.Image.Id));
        Assert.All(result.Entries, entry =>
        {
            Assert.Equal(ImageSource.UserUpload, entry.Image.Source);
            Assert.True(entry.Image.UploadedAtUtc >= seed.Start && entry.Image.UploadedAtUtc < seed.Start.AddDays(1));
            Assert.Null(entry.PeriodLikeCount);
            Assert.Null(entry.PeriodCommentCount);
        });
    }

    [Theory]
    [InlineData("most-liked")]
    [InlineData("new-uploads")]
    [InlineData("most-commented")]
    public async Task GetDiscovery_AiFiltersAndStaff_KeepGalleryTotalAndVisibilityUnchanged(string collection)
    {
        var seed = await SeedAsync();
        await using var app = new ApiFactory(database, seed.Clock);
        using var client = app.Client();
        await SignInAsync(client, seed, staff: true);
        var filtered = await ReadAsync(client,
            $"/api/discovery/{collection}?aiUsage=humanmade&aiUsage=AiGenerated&aiUsage=HumanMade");
        Assert.Equal(8, filtered.GalleryTotal);
        Assert.Equal(collection == "new-uploads" ? 3 : 4, filtered.Total);
        Assert.All(filtered.Entries, entry =>
        {
            Assert.Equal(ImageVisibility.Gallery, entry.Image.Visibility);
            Assert.Equal(ModerationStatus.Approved, entry.Image.ModerationStatus);
            Assert.Null(entry.Image.HiddenAtUtc);
            Assert.Null(entry.Image.PendingTags);
        });
        var empty = await ReadAsync(client, $"/api/discovery/{collection}?aiUsage=AiAssisted");
        Assert.Equal(8, empty.GalleryTotal);
        Assert.Equal(0, empty.Total);
        Assert.Empty(empty.Entries);
    }

    [Theory]
    [InlineData("most-liked")]
    [InlineData("new-uploads")]
    [InlineData("most-commented")]
    public async Task GetDiscovery_InactivePastDayAndEmptyGallery_ReturnsSuccessfulEmptyEnvelope(string collection)
    {
        var seed = await SeedAsync();
        await using var app = new ApiFactory(database, seed.Clock);
        using var client = app.Client();
        await SignInAsync(client, seed);
        var past = await ReadAsync(client, $"/api/discovery/{collection}?date={seed.Start.AddDays(-30):yyyy-MM-dd}");
        Assert.Equal(8, past.GalleryTotal);
        Assert.Equal(0, past.Total);
        Assert.Empty(past.Entries);

        await using var context = database.CreateContext();
        await context.Images.ExecuteDeleteAsync();
        var empty = await ReadAsync(client, "/api/discovery/" + collection);
        Assert.Equal(0, empty.GalleryTotal);
        Assert.Equal(0, empty.Total);
        Assert.Empty(empty.Entries);
    }

    [Fact]
    public async Task GetDiscovery_UnlikesAndRelikes_CountOnlyActiveLikesAndPastRecords()
    {
        var seed = await SeedAsync();
        await using var app = new ApiFactory(database, seed.Clock);
        using var client = app.Client();
        await SignInAsync(client, seed);

        async Task<DiscoveryEntryDto> Winner()
            => (await ReadAsync(client, "/api/discovery/most-liked")).Entries.Single(entry =>
                entry.Image.Id == seed.Winner.Id);

        Assert.Equal(2, (await Winner()).PeriodLikeCount);
        (await client.DeleteAsync($"/api/images/{seed.Winner.Id}/likes/me")).EnsureSuccessStatusCode();
        Assert.Equal(1, (await Winner()).PeriodLikeCount);
        await using (var context = database.CreateContext())
            await context.ImageLikes.Where(like => like.ImageId == seed.Winner.Id && like.CreatedAtUtc < seed.Start)
                .ExecuteDeleteAsync();
        Assert.Equal(1, (await Winner()).PeriodLikeCount);
        for (var i = 0; i < 3; i++)
        {
            (await client.PutAsync($"/api/images/{seed.Winner.Id}/likes/me", null)).EnsureSuccessStatusCode();
            (await client.PutAsync($"/api/images/{seed.Winner.Id}/likes/me", null)).EnsureSuccessStatusCode();
            Assert.Equal(2, (await Winner()).PeriodLikeCount);
            (await client.DeleteAsync($"/api/images/{seed.Winner.Id}/likes/me")).EnsureSuccessStatusCode();
            Assert.Equal(1, (await Winner()).PeriodLikeCount);
        }

        var yesterday = await ReadAsync(client, $"/api/discovery/most-liked?date={seed.Start.AddDays(-1):yyyy-MM-dd}");
        Assert.DoesNotContain(yesterday.Entries, entry => entry.Image.Id == seed.Winner.Id);
    }

    [Theory]
    [InlineData(DiscoveryCollection.MostLiked)]
    [InlineData(DiscoveryCollection.MostCommented)]
    public async Task GetDiscovery_ConcurrentChangeAfterGalleryCount_ReturnsOneConsistentSnapshot(
        DiscoveryCollection collection)
    {
        var seed = await SeedAsync();
        var interceptor = new AfterFirstCount(async () =>
        {
            await using var writer = database.CreateContext();
            await using var transaction = await writer.Database.BeginTransactionAsync();
            await writer.ImageLikes.Where(like => like.ImageId == seed.Winner.Id).ExecuteDeleteAsync();
            await writer.Comments.Where(comment => comment.ImageId == seed.Winner.Id).ExecuteDeleteAsync();
            await writer.Images.Where(image => image.Id == seed.Winner.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(image => image.DeletedAtUtc, seed.Clock.GetUtcNow()));
            await transaction.CommitAsync();
        });
        await using var reader = database.CreateContext(interceptor);
        var result = await new DiscoveryRepository(reader).GetCollectionAsync(collection, seed.Start,
            seed.Start.AddDays(1),
            seed.Viewer.Id, 1, 20);
        Assert.True(interceptor.Changed);
        Assert.Equal(8, result.GalleryTotal);
        Assert.Equal(5, result.Total);
        var winner = result.Entries[0];
        Assert.Equal(seed.Winner.Id, winner.Image.Id);
        Assert.Equal(4, winner.Image.LikeCount);
        Assert.Equal(4, winner.Image.CommentCount);
        Assert.Null(winner.Image.HiddenAtUtc);
        Assert.Equal(2,
            collection == DiscoveryCollection.MostLiked ? winner.PeriodLikeCount : winner.PeriodCommentCount);
    }

    private static async Task<PageableDiscoveryDto> ReadAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PageableDiscoveryDto>(JsonOptions))!;
    }

    private async Task SignInAsync(HttpClient client, Seed seed, bool staff = false)
    {
        await using var context = database.CreateContext();
        if (staff)
        {
            var user = await context.AppUsers.SingleAsync(user => user.Id == seed.Viewer.Id);
            user.Roles = [AppUserRole.User, AppUserRole.Admin];
            await context.SaveChangesAsync();
        }

        var login = await database.Authentication(context, seed.Clock)
            .LoginAsync(new(seed.Viewer.Login, "integration-password"));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.Response.AccessToken);
    }

    private async Task<Seed> SeedAsync()
    {
        var clock = new TestClock();
        var start = new DateTimeOffset(clock.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
        await using var context = database.CreateContext();
        await context.Images.ExecuteDeleteAsync();
        await context.Tags.ExecuteDeleteAsync();
        var account = await database.Authentication(context, clock).RegisterAsync(
            new("discovery" + Guid.NewGuid().ToString("N"), "integration-password"));
        var viewer = await context.AppUsers.SingleAsync(user => user.Id == account.Response.User.Id);
        var others = Enumerable.Range(0, 3).Select(_ => new AppUser
        {
            Login = "liker" + Guid.NewGuid().ToString("N"), NormalizedLogin = Guid.NewGuid().ToString("N"),
            PasswordHash = "unused", CreatedAtUtc = start
        }).ToArray();
        context.AppUsers.AddRange(others);
        await context.SaveChangesAsync();
        var winner = Image(start.AddDays(-10), viewer);
        winner.Tags = new[] { TagModerationStatus.Approved, TagModerationStatus.Pending, TagModerationStatus.Rejected }
            .Select(status => new ImageTag
            {
                Name = status == TagModerationStatus.Approved ? "approved" : "pending-secret-" + status,
                NormalizedName = status.ToString(), CreatedAtUtc = start, ModerationStatus = status
            }).ToList();
        winner.Likes =
        [
            Like(viewer.Id, start), Like(others[0].Id, start.AddDays(1).AddTicks(-1)),
            Like(others[1].Id, start.AddTicks(-1)), Like(others[2].Id, start.AddDays(1))
        ];
        winner.Comments =
        [
            Comment(viewer.Id, start), Comment(viewer.Id, start.AddDays(1).AddTicks(-1)),
            Comment(viewer.Id, start.AddTicks(-1)), Comment(viewer.Id, start.AddDays(1)),
            Comment(viewer.Id, start.AddMinutes(1), deleted: true)
        ];
        var olderTie = Image(start.AddDays(-2), viewer);
        olderTie.AiUsage = AiUsageClassification.AiGenerated;
        var newerTieLow = Image(start.AddHours(1), viewer);
        var newerTieHigh = Image(start.AddHours(1), viewer);
        var scraped = Image(start.AddHours(2), viewer);
        scraped.Source = ImageSource.Scraped;
        scraped.AiUsage = AiUsageClassification.Unknown;
        foreach (var image in new[] { olderTie, newerTieLow, newerTieHigh, scraped })
        {
            image.Likes = [Like(others[0].Id, start)];
            image.Comments = [Comment(viewer.Id, start)];
        }

        var noDaily = Image(start.AddDays(-1), viewer);
        noDaily.Likes = [Like(others[0].Id, start.AddTicks(-1)), Like(others[1].Id, start.AddDays(1))];
        noDaily.Comments =
        [
            Comment(viewer.Id, start.AddTicks(-1)), Comment(viewer.Id, start.AddDays(1)),
            Comment(viewer.Id, start, deleted: true)
        ];
        var startUpload = Image(start, viewer);
        var nextMidnight = Image(start.AddDays(1), viewer);
        var excluded = Enumerable.Range(0, 4).Select(_ => Image(start.AddHours(3), viewer)).ToArray();
        excluded[0].Visibility = ImageVisibility.Private;
        excluded[1].ModerationStatus = ModerationStatus.Pending;
        excluded[2].ModerationStatus = ModerationStatus.Rejected;
        excluded[3].DeletedAtUtc = start;
        foreach (var image in excluded)
        {
            image.Likes = [Like(viewer.Id, start)];
            image.Comments = [Comment(viewer.Id, start)];
        }

        context.Images.AddRange(new[]
                { winner, olderTie, newerTieLow, newerTieHigh, scraped, noDaily, startUpload, nextMidnight }
            .Concat(excluded));
        await context.SaveChangesAsync();
        return new Seed(clock, start, viewer, winner, olderTie, newerTieLow, newerTieHigh, scraped, startUpload);
    }

    private static UserMadeImage Image(DateTimeOffset uploadedAtUtc, AppUser uploader) => new()
    {
        Source = ImageSource.UserUpload, UploadedAtUtc = uploadedAtUtc, UploadedById = uploader.Id,
        Visibility = ImageVisibility.Gallery, ModerationStatus = ModerationStatus.Approved,
        AiUsage = AiUsageClassification.HumanMade, StorageKey = "uploads/private-storage-key.jpg",
        ContentType = "image/jpeg", Width = 1920, Height = 1080
    };

    private static ImageLike Like(int userId, DateTimeOffset createdAtUtc) => new()
        { UserId = userId, CreatedAtUtc = createdAtUtc };

    private static Comment Comment(int userId, DateTimeOffset createdAtUtc, bool deleted = false) => new()
    {
        AuthorId = userId, CreatedAtUtc = createdAtUtc, Content = "comment",
        DeletedAtUtc = deleted ? createdAtUtc : null
    };

    private sealed record Seed(
        TestClock Clock,
        DateTimeOffset Start,
        AppUser Viewer,
        AppImage Winner,
        AppImage OlderTie,
        AppImage NewerTieLow,
        AppImage NewerTieHigh,
        AppImage Scraped,
        AppImage StartUpload);

    private sealed class AfterFirstCount(Func<Task> change) : DbCommandInterceptor
    {
        public bool Changed { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!Changed && command.CommandText.StartsWith("SELECT count(*)", StringComparison.Ordinal))
            {
                Changed = true;
                await change();
            }

            return result;
        }
    }
}