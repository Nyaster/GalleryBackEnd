using System.Net;
using System.Net.Http.Json;
using Entities.Models;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GallerySiteIntegrationTests;

public sealed class ScrapedImageTagSyncTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset SourceDate = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SyncKeepsLocalTagsAndOnlyRemovesPlaceholderWhenARealTagIsAvailable()
    {
        var image = await SeedAsync(["none yet"]);
        await using (var database = fixture.CreateContext())
        {
            database.Tags.Add(new ImageTag
                { Name = "blocked", NormalizedName = "blocked", ModerationStatus = TagModerationStatus.Rejected });
            await database.SaveChangesAsync();
        }

        await SyncAsync(image.Id, "None Yet", "blocked");
        Assert.Equal(["none yet"], await TagsAsync(image.Id));
        await SyncAsync(image.Id, "  Landscape ", "none yet", "landscape", "blocked");
        Assert.Equal(["landscape"], await TagsAsync(image.Id));
        await SyncAsync(image.Id, "Portrait");
        await SyncAsync(image.Id, "portrait");
        await SyncAsync(image.Id);
        Assert.Equal(["landscape", "portrait"], await TagsAsync(image.Id));
    }

    [Fact]
    public async Task SyncRespectsGlobalRemovalEvenAfterTagIsReapproved()
    {
        await using var api = await RoleAssignmentTestApi.StartAsync(fixture);
        var image = await SeedAsync(["removed-globally"]);
        var path = $"/api/admin/tags/{Assert.Single(image.Tags).Id}/moderation";
        using var rejected = await api.Manager.PatchAsJsonAsync(path, new { status = "Rejected" });
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        await SyncAsync(image.Id, "removed-globally", "independent");
        using var approved = await api.Manager.PatchAsJsonAsync(path, new { status = "Approved" });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        await SyncAsync(image.Id, "removed-globally");
        Assert.Equal(["independent"], await TagsAsync(image.Id));
    }

    [Theory]
    [InlineData(ImageTagChangeStatus.Pending)]
    [InlineData(ImageTagChangeStatus.Approved)]
    public async Task SyncPreservesSnapshotForPendingApprovalAndExplicitRevert(ImageTagChangeStatus status)
    {
        await using var api = await RoleAssignmentTestApi.StartAsync(fixture);
        var change = new ImageTagChange
        {
            PreviousTags = ["before"], ProposedTags = ["after"], Status = status, Kind = ImageTagChangeKind.Replacement,
            EditedByLogin = "editor", CreatedAtUtc = SourceDate,
            AppliedAtUtc = status == ImageTagChangeStatus.Approved ? SourceDate : null
        };
        var image = await SeedAsync(status == ImageTagChangeStatus.Approved ? ["after"] : ["before"], change);
        await SyncAsync(image.Id, "unrelated");
        var decision = status == ImageTagChangeStatus.Approved ? "Revert" : "Approve";
        using var response =
            await api.Manager.PatchAsJsonAsync($"/api/admin/tag-changes/{change.Id}/moderation", new { decision });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(status == ImageTagChangeStatus.Approved ? "before" : "after",
            Assert.Single(await TagsAsync(image.Id)));
        await using var database = fixture.CreateContext();
        Assert.Equal(SourceDate,
            await database.Images.Where(row => row.Id == image.Id).Select(row => row.UploadedAtUtc).SingleAsync());
        Assert.False(await database.Tags.AnyAsync(tag => tag.Name == "unrelated"));
    }

    [Theory]
    [InlineData(ImageTagChangeStatus.Approved)]
    [InlineData(ImageTagChangeStatus.Reverted)]
    [InlineData(ImageTagChangeStatus.Rejected)]
    public async Task SyncAddsIndependentTagsAndRespectsAnEarlierRemovalOrDeclinedAddition(ImageTagChangeStatus status)
    {
        var change = new ImageTagChange
        {
            PreviousTags = status == ImageTagChangeStatus.Approved ? ["keep", "omitted"] : ["keep"],
            ProposedTags = status == ImageTagChangeStatus.Approved ? ["keep"] : ["keep", "omitted"],
            Status = status, Kind = ImageTagChangeKind.Replacement, EditedByLogin = "editor", CreatedAtUtc = SourceDate
        };
        // A later local edit makes the old approval ineligible for Revert.
        var image = await SeedAsync(["keep", "local"], change);
        await SyncAsync(image.Id, "omitted", "new");
        Assert.Equal(["keep", "local", "new"], await TagsAsync(image.Id));
    }

    [Fact]
    public async Task ModeratorCommittingDuringSyncWinsAndOtherImagesStillSynchronize()
    {
        await using var api = await RoleAssignmentTestApi.StartAsync(fixture);
        var image = await SeedAsync(["none yet"]);
        var other = await SeedAsync(["original"]);
        var intercept = new BeforeSave(async () =>
        {
            using var response =
                await api.Manager.PutAsJsonAsync($"/api/images/{image.Id}/tags", new { tags = new[] { "moderated" } });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        });
        await using (var database = fixture.CreateContext(intercept))
        {
            Assert.False(await ScrapedImageTagSync.TrySynchronizeAsync(database, image.Id, ["stale-addition"],
                SourceDate, SourceDate, CancellationToken.None));
        }

        await SyncAsync(other.Id, "fresh-addition");
        Assert.Equal(["moderated"], await TagsAsync(image.Id));
        Assert.Equal(["fresh-addition", "original"], await TagsAsync(other.Id));
        await using var check = fixture.CreateContext();
        Assert.False(await check.Tags.AnyAsync(tag => tag.NormalizedName == "stale-addition"));
    }

    private sealed class BeforeSave(Func<Task> onSave) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await onSave();
            return result;
        }
    }

    private async Task<SelebusImage> SeedAsync(string[] tags, ImageTagChange? change = null)
    {
        await using var database = fixture.CreateContext();
        var image = new SelebusImage
        {
            Source = ImageSource.Scraped, StorageKey = $"test/{Guid.NewGuid():N}", ContentType = "image/jpeg",
            UploadedAtUtc = SourceDate.AddDays(-1),
            Tags = await new AppImageRepository(database).GetOrCreateTagsAsync(tags, SourceDate)
        };
        if (change is not null) image.TagChanges.Add(change);
        database.Images.Add(image);
        await database.SaveChangesAsync();
        return image;
    }

    private async Task SyncAsync(int id, params string[] source)
    {
        await using var database = fixture.CreateContext();
        Assert.True(await ScrapedImageTagSync.TrySynchronizeAsync(database, id, ScrapedImageTagSync.SourceTags(source),
            SourceDate, DateTimeOffset.UtcNow, CancellationToken.None));
    }

    private async Task<string[]> TagsAsync(int id)
    {
        await using var database = fixture.CreateContext();
        return await database.Images.Where(row => row.Id == id)
            .SelectMany(row => row.Tags.Select(tag => tag.NormalizedName))
            .Order().ToArrayAsync();
    }
}