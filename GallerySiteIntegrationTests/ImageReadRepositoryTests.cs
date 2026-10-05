using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Repository;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace GallerySiteIntegrationTests;

public sealed class ImageReadRepositoryTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task ImageCards_EngagedImages_ProjectsCountsAndOnlyRequestedPage()
    {
        var seed = await SeedAsync();
        var capture = new QueryCapture();
        await using var context = database.CreateContext(capture);
        var repository = new AppImageRepository(context);
        var owner = new ImageViewer(seed.OwnerId, false);
        var visitor = new ImageViewer(seed.VisitorId, false);

        var search = await repository.SearchAsync(new SearchImageDto(null, PageSize: 2), visitor);
        Assert.Equal(4, search.Total);
        Assert.Equal(new[] { seed.PublicIds[0], seed.PublicIds[1] }, search.Images.Select(image => image.Id));
        var card = search.Images[0];
        Assert.Equal(60, card.LikeCount);
        Assert.Equal(30, card.CommentCount);
        Assert.True(card.IsLikedByCurrentUser);
        Assert.False(card.CanSeePendingTags);
        Assert.Equal("owner", card.UploadedBy);
        Assert.Equal(new[] { "approved" }, card.Tags.Select(tag => tag.Name));
        Assert.Equal(2, capture.Reads.Count);
        Assert.Contains("LIMIT", capture.Reads[1]);
        Assert.Empty(capture.Entities);
        Assert.All(capture.Reads, sql =>
        {
            Assert.DoesNotContain("\"Embedding\"", sql);
            Assert.DoesNotContain("\"PasswordHash\"", sql);
            Assert.DoesNotContain("\"Content\"", sql);
        });

        var owned = (await repository.GetCardByIdAsync(seed.PublicIds[0], owner))!;
        Assert.True(owned.CanSeePendingTags);
        Assert.Equal(new[] { "approved", "pending" }, owned.Tags.Select(tag => tag.Name));
        var staff = (await repository.GetCardByIdAsync(seed.PublicIds[0], new ImageViewer(seed.VisitorId, true)))!;
        Assert.True(staff.CanSeePendingTags);
        Assert.Equal(owned.Tags, staff.Tags);

        var mine = await repository.GetUploadedByUserAsync(seed.OwnerId, true, 2, 2, owner);
        Assert.Equal(7, mine.Total);
        Assert.Equal(2, mine.Images.Count);
        Assert.All(mine.Images, image => Assert.True(image.CanSeePendingTags));
        var visibleMine = await repository.GetUploadedByUserAsync(seed.OwnerId, false, 1, 50, owner);
        Assert.Equal(6, visibleMine.Total);

        var liked = await repository.GetLikedByUserAsync(seed.VisitorId, 1, 1, visitor);
        Assert.Equal(2, liked.Total);
        Assert.Equal(seed.PublicIds[1], Assert.Single(liked.Images).Id);
        var filteredLiked = await repository.GetLikedByUserAsync(seed.VisitorId, 1, 50, visitor,
            aiUsage: [AiUsageClassification.AiGenerated]);
        Assert.Equal(seed.PublicIds[1], Assert.Single(filteredLiked.Images).Id);

        capture.Clear();
        var recommendations = await repository.GetRecommendationsAsync(seed.PublicIds[0], 1, 2, visitor);
        Assert.Equal(3, recommendations.Total);
        Assert.Equal(new[] { seed.PublicIds[1], seed.PublicIds[2] }, recommendations.Images.Select(image => image.Id));
        Assert.Equal(3, capture.Reads.Count);
        // Only the source vector is transferred; recommendation cards omit vectors and engagement entities.
        Assert.All(capture.Reads.Skip(1), sql => Assert.DoesNotContain("SELECT i.\"Embedding\"", sql));
        Assert.Empty(capture.Entities);

        var pending = await repository.GetPendingAsync(1, 2, new ImageViewer(seed.OwnerId, true));
        Assert.Equal(seed.PendingId, Assert.Single(pending).Id);
        var hidden = await repository.GetHiddenAsync(1, 2, owner);
        Assert.Equal(seed.HiddenId, Assert.Single(hidden).Id);
        Assert.Empty((await repository.SearchAsync(new SearchImageDto(null, Page: int.MaxValue, PageSize: 50), visitor)).Images);
        Assert.Empty(await repository.GetPendingAsync(int.MaxValue, 50, owner));
        Assert.Empty(await repository.GetHiddenAsync(int.MaxValue, 50, owner));
        var official = await repository.SearchAsync(new SearchImageDto(["approved"], Kind: ImageKind.Official), visitor);
        Assert.Equal(seed.PublicIds[3], Assert.Single(official.Images).Id);
        var fan = await repository.SearchAsync(new SearchImageDto(null, Kind: ImageKind.Fan,
            AiUsage: [AiUsageClassification.AiGenerated]), visitor);
        Assert.Equal(seed.PublicIds[1], Assert.Single(fan.Images).Id);
        var excluded = await repository.SearchAsync(new SearchImageDto(null, ExcludedTags: ["approved"]), visitor);
        Assert.Equal(0, excluded.Total);
        Assert.Empty(excluded.Images);
        var oldest = await repository.SearchAsync(new SearchImageDto(null, Sort: ImageSort.Oldest, PageSize: 1), visitor);
        Assert.Equal(seed.PublicIds[3], Assert.Single(oldest.Images).Id);
        var media = await repository.SearchAsync(new SearchImageDto(null, Sort: ImageSort.MediaId, PageSize: 1), visitor);
        // Preserve PostgreSQL's existing descending order: null media IDs precede scraped IDs.
        Assert.Equal(seed.PublicIds[0], Assert.Single(media.Images).Id);
        var randomRequest = new SearchImageDto(null, Sort: ImageSort.Random, PageSize: 2, RandomSeed: "AAAAAAAAAAA");
        var randomFirst = await repository.SearchAsync(randomRequest, visitor);
        var randomNext = await repository.SearchAsync(randomRequest with { Page = 2 }, visitor);
        Assert.Equal(4, randomFirst.Images.Concat(randomNext.Images).Select(image => image.Id).Distinct().Count());
        Assert.Empty(capture.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ImageMetadata_AccessChecks_PreserveOwnerStaffAndVisitorBoundaries()
    {
        var seed = await SeedAsync();
        var capture = new QueryCapture();
        await using var context = database.CreateContext(capture);
        var repositories = new RepositoryManager(context);
        var owner = new Application.Features.Images.GetImageById.Handler(repositories, new Viewer(seed.OwnerId));
        var visitor = new Application.Features.Images.GetImageById.Handler(repositories, new Viewer(seed.VisitorId));
        var staff = new Application.Features.Images.GetImageById.Handler(repositories, new Viewer(seed.VisitorId, true));

        Assert.NotNull((await owner.Handle(new(seed.PrivateId), CancellationToken.None)).PendingTags);
        Assert.NotNull((await staff.Handle(new(seed.PendingId), CancellationToken.None)).PendingTags);
        Assert.Null((await visitor.Handle(new(seed.PublicIds[0]), CancellationToken.None)).PendingTags);
        await Assert.ThrowsAsync<AppForbiddenException>(() => visitor.Handle(new(seed.PrivateId), CancellationToken.None));
        await Assert.ThrowsAsync<AppForbiddenException>(() => visitor.Handle(new(seed.PendingId), CancellationToken.None));
        await Assert.ThrowsAsync<Base404ReturnException>(() => visitor.Handle(new(seed.HiddenId), CancellationToken.None));
        Assert.NotNull(await owner.Handle(new(seed.HiddenId), CancellationToken.None));
        Assert.NotNull(await staff.Handle(new(seed.HiddenId), CancellationToken.None));
        await Assert.ThrowsAsync<Base404ReturnException>(() => owner.Handle(new(int.MaxValue), CancellationToken.None));

        capture.Clear();
        var metadata = (await repositories.AppImage.GetMetadataByIdAsync(seed.PublicIds[0]))!;
        Assert.True(metadata.HasEmbedding);
        Assert.Equal(seed.OwnerId, metadata.UploadedById);
        Assert.Single(capture.Reads);
        Assert.DoesNotContain("JOIN", capture.Reads[0]);
        Assert.DoesNotContain("\"PasswordHash\"", capture.Reads[0]);
        Assert.Empty(capture.Entities);
        Assert.Empty(context.ChangeTracker.Entries());

        var scalar = (await repositories.AppImage.GetByIdAsync(seed.PublicIds[0], true))!;
        Assert.Empty(scalar.Tags);
        Assert.Empty(scalar.Likes);
        Assert.Empty(scalar.Comments);
        Assert.Null(scalar.UploadedBy);
        var withTags = (await repositories.AppImage.GetWithTagsByIdAsync(seed.PublicIds[0]))!;
        Assert.Equal(3, withTags.Tags.Count);
        Assert.Empty(withTags.Likes);
        Assert.Empty(withTags.Comments);
        var scraped = await repositories.AppImage.GetByExternalMediaIdsAsync([123], true);
        Assert.Equal(3, Assert.Single(scraped).Tags.Count);
        Assert.Empty(scraped[0].Likes);
        Assert.Empty(scraped[0].Comments);
    }

    [Fact]
    public async Task ImageMutations_ReturnCards_PreserveEngagementCountsAndTagPermissions()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        var repositories = new RepositoryManager(context);
        var owner = new Viewer(seed.OwnerId);
        await new Application.Features.Images.HideImage.Handler(repositories, owner, TimeProvider.System)
            .Handle(new(seed.PublicIds[0]), CancellationToken.None);
        var restored = await new Application.Features.Images.RestoreImage.Handler(repositories, owner)
            .Handle(new(seed.PublicIds[0]), CancellationToken.None);
        Assert.Equal(ModerationStatus.Pending, restored.ModerationStatus);
        Assert.Null(restored.HiddenAtUtc);
        Assert.Equal(60, restored.LikeCount);
        Assert.Equal(30, restored.CommentCount);
        Assert.Equal(new[] { "approved" }, restored.Tags);
        Assert.Equal(new[] { "pending" }, restored.PendingTags);

        var approved = await new Application.Features.Administration.ChangeModeration.Handler(repositories,
                new Viewer(seed.VisitorId, true))
            .Handle(new(seed.PublicIds[0], ModerationStatus.Approved, AiUsageClassification.AiAssisted), CancellationToken.None);
        Assert.Equal(60, approved.LikeCount);
        Assert.Equal(30, approved.CommentCount);
        Assert.True(approved.IsLikedByCurrentUser);
        Assert.Equal(AiUsageClassification.AiAssisted, approved.AiUsage);
        Assert.Equal(new[] { "pending" }, approved.PendingTags);
        var published = await new Application.Features.Images.PublishImage.Handler(repositories, owner)
            .Handle(new(seed.PrivateId), CancellationToken.None);
        Assert.Equal(ImageVisibility.Gallery, published.Visibility);
        Assert.Equal(ModerationStatus.Approved, published.ModerationStatus);
        Assert.Equal(new[] { "pending" }, published.PendingTags);
    }

    [Fact]
    public async Task ImageEndpoints_AuthenticatedViewer_PreserveCardsLikesAndAccessResponses()
    {
        var seed = await SeedAsync();
        var clock = new TestClock();
        string token;
        await using (var context = database.CreateContext())
        {
            var login = await database.Authentication(context, clock).RegisterAsync(
                new("imagereader" + Guid.NewGuid().ToString("N"), "integration-password"));
            token = login.Response.AccessToken;
        }
        await using var app = new ApiFactory(database, clock);
        using var client = app.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/images")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var like = await client.PutAsync($"/api/images/{seed.PublicIds[0]}/likes/me", null);
        Assert.Equal(HttpStatusCode.OK, like.StatusCode);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        var search = (await client.GetFromJsonAsync<PageableImagesDto>("/api/images?pageSize=1", json))!;
        Assert.Equal(4, search.Total);
        var card = Assert.Single(search.Images);
        Assert.Equal(61, card.LikeCount);
        Assert.Equal(30, card.CommentCount);
        Assert.True(card.IsLikedByCurrentUser);
        Assert.Null(card.PendingTags);
        Assert.Equal(new[] { "approved" }, card.Tags);
        Assert.Equal(seed.PublicIds[0], card.Id);
        var liked = (await client.GetFromJsonAsync<PageableLikedImagesDto>("/api/users/me/liked-images", json))!;
        Assert.Equal(1, liked.Total);
        Assert.Equal(card.Id, Assert.Single(liked.Images).Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/images/{seed.PrivateId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/images/{seed.HiddenId}")).StatusCode);
    }

    private async Task<Seed> SeedAsync()
    {
        await using var context = database.CreateContext();
        await context.Images.ExecuteDeleteAsync();
        await context.Tags.ExecuteDeleteAsync();
        var now = DateTimeOffset.UtcNow;
        var owner = User("owner", now);
        var likers = Enumerable.Range(0, 60).Select(index => User("liker" + index, now)).ToArray();
        context.AppUsers.Add(owner);
        context.AppUsers.AddRange(likers);
        await context.SaveChangesAsync();
        var tags = new[] { TagModerationStatus.Approved, TagModerationStatus.Pending, TagModerationStatus.Rejected }
            .Select(status => new ImageTag { Name = status.ToString().ToLowerInvariant(),
                NormalizedName = status.ToString().ToLowerInvariant(), ModerationStatus = status, CreatedAtUtc = now }).ToList();
        var images = Enumerable.Range(0, 7).Select(index => new UserMadeImage
        {
            Source = ImageSource.UserUpload, UploadedById = owner.Id, UploadedAtUtc = now.AddMinutes(-index),
            Visibility = index == 4 ? ImageVisibility.Private : ImageVisibility.Gallery,
            ModerationStatus = index == 5 ? ModerationStatus.Pending : ModerationStatus.Approved,
            DeletedAtUtc = index == 6 ? now : null, StorageKey = $"uploads/{Guid.NewGuid():N}.jpg",
            ContentType = "image/jpeg", Width = 100, Height = 80, AiUsage = index == 1
                ? AiUsageClassification.AiGenerated : AiUsageClassification.HumanMade,
            Tags = tags.ToList(), Embedding = new Vector(Enumerable.Repeat((float)index, 1024).ToArray())
        }).Cast<AppImage>().ToList();
        images[3] = new SelebusImage
        {
            Source = ImageSource.Scraped, ExternalMediaId = 123, UploadedById = owner.Id,
            UploadedAtUtc = now.AddMinutes(-3), Visibility = ImageVisibility.Gallery,
            ModerationStatus = ModerationStatus.Approved, StorageKey = "scraped/123.jpg", ContentType = "image/jpeg",
            Width = 100, Height = 80, Tags = tags.ToList(), Embedding = new Vector(Enumerable.Repeat(3f, 1024).ToArray())
        };
        images[0].Likes = likers.Select(user => new ImageLike { UserId = user.Id, CreatedAtUtc = now.AddDays(-1) }).ToList();
        images[1].Likes = [new ImageLike { UserId = likers[0].Id, CreatedAtUtc = now }];
        images[0].Comments = Enumerable.Range(0, 60).Select(index => new Comment
        {
            AuthorId = owner.Id, Content = "comment", CreatedAtUtc = now,
            DeletedAtUtc = index % 2 == 0 ? now : null
        }).ToList();
        context.Images.AddRange(images);
        await context.SaveChangesAsync();
        return new Seed(owner.Id, likers[0].Id, images.Take(4).Select(image => image.Id).ToArray(),
            images[4].Id, images[5].Id, images[6].Id);
    }

    private static AppUser User(string login, DateTimeOffset now) => new()
    {
        Login = login, NormalizedLogin = Guid.NewGuid().ToString("N"), PasswordHash = "unused", CreatedAtUtc = now
    };

    private sealed record Seed(int OwnerId, int VisitorId, int[] PublicIds, int PrivateId, int PendingId, int HiddenId);

    private sealed class Viewer(int id, bool staff = false) : IUserContext
    {
        public int? UserId => id;
        public string? Login => "viewer";
        public bool IsInRole(string role) => staff && role == "Moderator";
        public void RequireAuthenticated() { }
    }
}
