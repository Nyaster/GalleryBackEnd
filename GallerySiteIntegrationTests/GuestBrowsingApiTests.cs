using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Entities.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pgvector;
using Shared.DataTransferObjects;

namespace GallerySiteIntegrationTests;

public sealed class GuestBrowsingApiTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    [Fact]
    public async Task GuestDiscovery_EligibleUploads_AppliesVisibilityBeforeTotalsAndPaging()
    {
        var seed = await SeedAsync();
        await using var app = new ApiFactory(database, seed.Clock);
        using var client = app.Client();
        var first = await ReadAsync<PageableImagesDto>(client, "/api/images?pageSize=1");
        var second = await ReadAsync<PageableImagesDto>(client, "/api/images?pageSize=1&page=2");
        Assert.Equal(2, first.Total);
        Assert.Equal(seed.EligibleIds.Order(), first.Images.Concat(second.Images).Select(image => image.Id).Order());
        Assert.All(first.Images.Concat(second.Images), image =>
        {
            Assert.Equal(ImageSource.UserUpload, image.Source);
            Assert.False(image.IsLikedByCurrentUser);
            Assert.Null(image.PendingTags);
            Assert.Contains("sfw", image.Tags);
        });
        Assert.Empty((await ReadAsync<PageableImagesDto>(client, "/api/images?kind=Official")).Images);
        Assert.Equal(2, (await ReadAsync<PageableImagesDto>(client, "/api/images?kind=Fan")).Total);
        Assert.Equal(1, (await ReadAsync<PageableImagesDto>(client, "/api/images?aiUsage=AiGenerated")).Total);
        Assert.Empty((await ReadAsync<PageableImagesDto>(client, "/api/images?excludedTags=sfw")).Images);
        Assert.Empty((await ReadAsync<PageableImagesDto>(client, "/api/images?tags=restricted-topic")).Images);
        var random = await ReadAsync<PageableImagesDto>(client, "/api/images?sort=Random&pageSize=1");
        var next = await ReadAsync<PageableImagesDto>(client,
            $"/api/images?sort=Random&pageSize=1&page=2&randomSeed={random.RandomSeed}");
        Assert.NotEqual(Assert.Single(random.Images).Id, Assert.Single(next.Images).Id);

        foreach (var collection in new[] { "most-liked", "most-commented", "new-uploads" })
        {
            var daily = await ReadAsync<PageableDiscoveryDto>(client, $"/api/discovery/{collection}");
            Assert.Equal(2, daily.GalleryTotal);
            Assert.Equal(2, daily.Total);
            Assert.Equal(seed.EligibleIds.Order(), daily.Entries.Select(entry => entry.Image.Id).Order());
            Assert.All(daily.Entries, entry => Assert.False(entry.Image.IsLikedByCurrentUser));
            var filtered = await ReadAsync<PageableDiscoveryDto>(client,
                $"/api/discovery/{collection}?aiUsage=AiGenerated");
            Assert.Equal(2, filtered.GalleryTotal);
            Assert.Equal(1, filtered.Total);
        }

        var tags = await ReadAsync<List<TagsDto>>(client, "/api/tags/suggestions?query=g");
        Assert.Equal(new[] { "guest-topic" }, tags.Select(tag => tag.Name));
        Assert.Empty(await ReadAsync<List<TagsDto>>(client, "/api/tags/suggestions?query=restricted"));
        using var response = await client.GetAsync("/api/images");
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.Contains("Authorization", response.Headers.Vary);
    }

    [Fact]
    public async Task GuestImageReads_Eligibility_AppliesToDetailsFilesCommentsAndRecommendations()
    {
        var seed = await SeedAsync();
        await using var app = new ApiFactory(database, seed.Clock);
        using var client = app.Client();
        var id = seed.EligibleIds[0];
        var image = await ReadAsync<AppImageDto>(client, $"/api/images/{id}");
        Assert.Equal(1, image.LikeCount);
        Assert.Equal(1, image.CommentCount);
        var comments = await ReadAsync<PageableCommentsDto>(client, $"/api/images/{id}/comments");
        Assert.Equal("public comment", Assert.Single(comments.Comments).Content);
        var related = await ReadAsync<PageableRecommendationsDto>(client, $"/api/images/{id}/recommendations");
        Assert.Equal(seed.EligibleIds[1], Assert.Single(related.Images).Id);
        Assert.Equal(1, related.Total);
        foreach (var format in new[] { "original", "jpeg", "preview" })
        {
            using var response = await client.GetAsync($"/api/images/{id}/content?format={format}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
            Assert.True(response.Headers.CacheControl?.NoStore);
        }

        foreach (var deniedId in seed.DeniedIds.Append(int.MaxValue))
        foreach (var suffix in new[]
                 {
                     "", "/comments", "/recommendations", "/content?format=original",
                     "/content?format=jpeg", "/content?format=preview"
                 })
        {
            using var response = await client.GetAsync($"/api/images/{deniedId}{suffix}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
    }

    [Fact]
    public async Task GuestAuthorization_WritesAndOtherRoutes_RequireSignIn()
    {
        var seed = await SeedAsync();
        await using var app = new ApiFactory(database, seed.Clock);
        using var client = app.Client();
        foreach (var path in new[]
                 {
                     "/api/users/me/liked-images", "/api/rankings/Daily", "/api/announcement",
                     $"/api/images/{seed.EligibleIds[0]}/tag-changes"
                 })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        var id = seed.EligibleIds[0];
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PutAsJsonAsync($"/api/images/{id}/likes/me", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync($"/api/images/{id}/likes/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync($"/api/images/{id}/comments", new { content = "guest" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsync("/api/images", new MultipartFormDataContent())).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/images")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.Token);
        var signedIn = await ReadAsync<PageableImagesDto>(client, "/api/images?kind=Official");
        Assert.Single(signedIn.Images);
        using var signedInResponse = await client.GetAsync("/api/images");
        Assert.Contains("Authorization", signedInResponse.Headers.Vary);
        client.DefaultRequestHeaders.Authorization = null;
        var guestAgain = await ReadAsync<PageableImagesDto>(client, "/api/images");
        Assert.Equal(2, guestAgain.Total);
        Assert.All(guestAgain.Images, image => Assert.False(image.IsLikedByCurrentUser));

        await using var context = database.CreateContext();
        var expiredClock = new TestClock();
        expiredClock.Advance(TimeSpan.FromHours(-1));
        var user = await context.AppUsers.SingleAsync(item => item.Id == seed.UserId);
        var expired = await database.Authentication(context, expiredClock)
            .LoginAsync(new(user.Login, "integration-password"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expired.Response.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/images")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.Token);
        await context.AppUsers.Where(item => item.Id == seed.UserId)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.AuthenticationVersion, 1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/images")).StatusCode);
    }

    [Fact]
    public async Task GuestVisibility_SafetyModerationChanges_RechecksFilesAndCachedPreviews()
    {
        var seed = await SeedAsync();
        await using var app = new ApiFactory(database, seed.Clock);
        using var client = app.Client();
        var id = seed.EligibleIds[0];
        using var preview = await client.GetAsync($"/api/images/{id}/content?format=preview");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        await using var context = database.CreateContext();
        var sfw = await context.Tags.SingleAsync(tag => tag.NormalizedName == "sfw");
        foreach (var status in new[] { TagModerationStatus.Pending, TagModerationStatus.Rejected })
        {
            sfw.ModerationStatus = status;
            await context.SaveChangesAsync();
            Assert.Empty((await ReadAsync<PageableImagesDto>(client, "/api/images")).Images);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/images/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await client.GetAsync($"/api/images/{id}/content?format=preview")).StatusCode);
        }

        sfw.ModerationStatus = TagModerationStatus.Approved;
        await context.SaveChangesAsync();
        var image = await context.Images.Include(item => item.Tags).SingleAsync(item => item.Id == id);
        var nsfw = await context.Tags.SingleAsync(tag => tag.NormalizedName == "nsfw");
        image.Tags.Add(nsfw);
        await context.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/images/{id}/content?format=preview")).StatusCode);
        nsfw.ModerationStatus = TagModerationStatus.Approved;
        await context.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/images/{id}")).StatusCode);
        image.Tags.Remove(nsfw);
        await context.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/images/{id}/content?format=preview")).StatusCode);

        var proposal = await context.ImageTagChanges.SingleAsync();
        var proposedImageId = proposal.ImageId;
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/images/{proposedImageId}")).StatusCode);
        proposal.Status = ImageTagChangeStatus.Rejected;
        await context.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/images/{proposedImageId}")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.Token);
        using var authenticated = await client.GetAsync($"/api/images/{id}/content?format=preview");
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        Assert.Contains("Authorization", authenticated.Headers.Vary);
    }

    [Fact]
    public async Task GuestRateLimit_ConfiguredBudget_IsSharedAcrossReadsAndExemptsSignedInUsers()
    {
        var seed = await SeedAsync();
        await using var app = new ApiFactory(database, seed.Clock).WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["GuestBrowsing:RequestsPerMinute"] = "2" })));
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://gallery.test") });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/images")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tags/suggestions?query=s")).StatusCode);
        using var limited = await client.GetAsync($"/api/images/{seed.EligibleIds[0]}/content?format=preview");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.CacheControl?.NoStore);
        using var problem = JsonDocument.Parse(await limited.Content.ReadAsStringAsync());
        Assert.Equal(429, problem.RootElement.GetProperty("status").GetInt32());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/images")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/images")).StatusCode);
    }

    [Fact]
    public async Task GuestRecommendations_EligibleImageWithoutEmbedding_ReturnsConflict()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        await context.Images.Where(image => image.Id == seed.EligibleIds[0])
            .ExecuteUpdateAsync(update => update.SetProperty(image => image.Embedding, (Vector?)null));
        await using var app = new ApiFactory(database, seed.Clock);
        using var client = app.Client();
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.GetAsync($"/api/images/{seed.EligibleIds[0]}/recommendations")).StatusCode);
    }

    [Fact]
    public async Task GuestTagSuggestions_Popularity_OnlyCountsEligibleImages()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        var hiddenPopular = new ImageTag
        {
            Name = "rank-a", NormalizedName = "rank-a", ModerationStatus = TagModerationStatus.Approved,
            CreatedAtUtc = seed.Clock.GetUtcNow()
        };
        var guestPopular = new ImageTag
        {
            Name = "rank-z", NormalizedName = "rank-z", ModerationStatus = TagModerationStatus.Approved,
            CreatedAtUtc = seed.Clock.GetUtcNow()
        };
        var images = await context.Images.Include(image => image.Tags).ToListAsync();
        foreach (var image in images)
        {
            if (image.Id != seed.EligibleIds[1]) image.Tags.Add(hiddenPopular);
            if (seed.EligibleIds.Contains(image.Id)) image.Tags.Add(guestPopular);
        }
        await context.SaveChangesAsync();
        await using var app = new ApiFactory(database, seed.Clock);
        using var client = app.Client();

        var tags = await ReadAsync<List<TagsDto>>(client, "/api/tags/suggestions?query=rank");

        Assert.Equal(new[] { "rank-z", "rank-a" }, tags.Select(tag => tag.Name));
    }

    private static async Task<T> ReadAsync<T>(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private async Task<Seed> SeedAsync()
    {
        var clock = new TestClock();
        await using var context = database.CreateContext();
        await context.ImageTagChanges.ExecuteDeleteAsync();
        await context.Images.ExecuteDeleteAsync();
        await context.Tags.ExecuteDeleteAsync();
        var account = await database.Authentication(context, clock).RegisterAsync(
            new("guesttest" + Guid.NewGuid().ToString("N"), "integration-password"));
        var userId = account.Response.User.Id;

        ImageTag Tag(string name, TagModerationStatus status = TagModerationStatus.Approved)
            => new()
            {
                Name = name, NormalizedName = name, ModerationStatus = status, CreatedAtUtc = clock.GetUtcNow()
            };

        var sfw = Tag("sfw");
        var nsfw = Tag("nsfw", TagModerationStatus.Pending);
        var topic = Tag("guest-topic");
        var restricted = Tag("restricted-topic");

        UserMadeImage Upload(params ImageTag[] tags) => new()
        {
            Source = ImageSource.UserUpload, UploadedById = userId, UploadedAtUtc = clock.GetUtcNow(),
            Visibility = ImageVisibility.Gallery, ModerationStatus = ModerationStatus.Approved,
            AiUsage = AiUsageClassification.HumanMade, StorageKey = Guid.NewGuid().ToString("N") + ".png",
            ContentType = "image/png", Width = 1, Height = 1, Tags = tags.ToList(),
            Embedding = new Vector(new float[1024]), EmbeddingStatus = EmbeddingStatus.Ready
        };

        var first = Upload(sfw, topic);
        var second = Upload(sfw, topic);
        second.AiUsage = AiUsageClassification.AiGenerated;
        var privateImage = Upload(sfw, restricted);
        privateImage.Visibility = ImageVisibility.Private;
        var pending = Upload(sfw, restricted);
        pending.ModerationStatus = ModerationStatus.Pending;
        var rejected = Upload(sfw, restricted);
        rejected.ModerationStatus = ModerationStatus.Rejected;
        var deleted = Upload(sfw, restricted);
        deleted.DeletedAtUtc = clock.GetUtcNow();
        var proposedUnsafe = Upload(sfw, restricted);
        proposedUnsafe.TagChanges.Add(new ImageTagChange
        {
            PreviousTags = ["sfw", "restricted-topic"], ProposedTags = ["sfw", "nsfw"],
            Kind = ImageTagChangeKind.Replacement, Status = ImageTagChangeStatus.Pending,
            EditedByUserId = userId, EditedByLogin = "uploader", CreatedAtUtc = clock.GetUtcNow()
        });
        var scraped = new SelebusImage
        {
            Source = ImageSource.Scraped, UploadedAtUtc = clock.GetUtcNow(), Visibility = ImageVisibility.Gallery,
            ModerationStatus = ModerationStatus.Approved, StorageKey = "scraped.png", ContentType = "image/png",
            Width = 1, Height = 1, Tags = [sfw, restricted], Embedding = new Vector(new float[1024])
        };
        AppImage[] denied =
        [
            scraped, Upload(restricted), Upload(sfw, nsfw, restricted), privateImage, pending,
            rejected, deleted, proposedUnsafe, Upload(Tag("almost-sfw"), restricted)
        ];
        var all = new AppImage[] { first, second }.Concat(denied).ToArray();
        foreach (var image in all)
        {
            image.Likes.Add(new ImageLike { UserId = userId, CreatedAtUtc = clock.GetUtcNow() });
            image.Comments.Add(new Comment
                { AuthorId = userId, Content = "public comment", CreatedAtUtc = clock.GetUtcNow() });
        }

        context.Images.AddRange(all);
        await context.SaveChangesAsync();
        var root = Path.Combine(database.RootPath, "images");
        Directory.CreateDirectory(root);
        using var bitmap = new SkiaSharp.SKBitmap(1, 1);
        bitmap.Erase(SkiaSharp.SKColors.Blue);
        using var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        foreach (var image in all)
            await File.WriteAllBytesAsync(Path.Combine(root, image.StorageKey), data.ToArray());
        return new Seed(clock, [first.Id, second.Id], denied.Select(image => image.Id).ToArray(),
            userId, account.Response.AccessToken);
    }

    private sealed record Seed(TestClock Clock, int[] EligibleIds, int[] DeniedIds, int UserId, string Token);
}