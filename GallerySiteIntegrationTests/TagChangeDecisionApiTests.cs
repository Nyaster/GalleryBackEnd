using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Entities.Models;
using Microsoft.EntityFrameworkCore;

namespace GallerySiteIntegrationTests;

public sealed class TagChangeDecisionApiTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task RejectAfterApproveLeavesTagsAppliedUntilExplicitRevert()
    {
        await using var api = await RoleAssignmentTestApi.StartAsync(fixture);
        var change = await SeedPendingChangeAsync(api.MemberId);
        var path = $"/api/admin/tag-changes/{change.Id}/moderation";

        using var legacy = await api.Manager.PatchAsJsonAsync(path, new { status = "Approved" });
        Assert.Equal(HttpStatusCode.BadRequest, legacy.StatusCode);

        using var approved = await api.Manager.PatchAsJsonAsync(path, new { decision = "Approve", note = "approved" });
        await AssertDecisionAsync(approved, ImageTagChangeStatus.Approved);

        using var rejected = await api.Manager.PatchAsJsonAsync(path, new { decision = "Reject", note = "stale" });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        await AssertPersistedChangeAsync(change, ImageTagChangeStatus.Approved, "approved", null);

        using var reverted = await api.Manager.PatchAsJsonAsync(path, new { decision = "Revert", note = "reverted" });
        await AssertDecisionAsync(reverted, ImageTagChangeStatus.Reverted);
        await AssertPersistedChangeAsync(change, ImageTagChangeStatus.Reverted, "approved", "reverted");
    }

    [Fact]
    public async Task CompetingApproveAndRejectPreserveOnlyTheWinningDecision()
    {
        await using var api = await RoleAssignmentTestApi.StartAsync(fixture);
        var change = await SeedPendingChangeAsync(api.MemberId);
        var path = $"/api/admin/tag-changes/{change.Id}/moderation";

        var responses = await Task.WhenAll(
            api.Manager.PatchAsJsonAsync(path, new { decision = "Approve", note = "approved" }),
            api.Manager.PatchAsJsonAsync(path, new { decision = "Reject", note = "rejected" }));
        using var approval = responses[0];
        using var rejection = responses[1];
        var winner = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);

        var approved = ReferenceEquals(winner, approval);
        var status = approved ? ImageTagChangeStatus.Approved : ImageTagChangeStatus.Rejected;
        await AssertDecisionAsync(winner, status);
        await AssertPersistedChangeAsync(change, status, approved ? "approved" : "rejected", null);
    }

    private async Task<ImageTagChange> SeedPendingChangeAsync(int ownerId)
    {
        await using var database = fixture.CreateContext();
        var previous = $"previous-{ownerId}";
        var proposed = $"proposed-{ownerId}";
        var now = DateTimeOffset.UtcNow;
        var change = new ImageTagChange
        {
            Kind = ImageTagChangeKind.Replacement, Status = ImageTagChangeStatus.Pending,
            PreviousTags = [previous], ProposedTags = [proposed], EditedByUserId = ownerId,
            EditedByLogin = "uploader", CreatedAtUtc = now
        };
        database.Images.Add(new UserMadeImage
        {
            StorageKey = $"uploads/tag-change-{ownerId}.jpg", ContentType = "image/jpeg", UploadedById = ownerId,
            UploadedAtUtc = now, Visibility = ImageVisibility.Gallery, ModerationStatus = ModerationStatus.Approved,
            Tags = [new ImageTag { Name = previous, NormalizedName = previous, ModerationStatus = TagModerationStatus.Approved }],
            TagChanges = [change]
        });
        await database.SaveChangesAsync();
        return change;
    }

    private static async Task AssertDecisionAsync(HttpResponseMessage response, ImageTagChangeStatus status)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status.ToString(), result.GetProperty("status").GetString());
    }

    private async Task AssertPersistedChangeAsync(ImageTagChange change, ImageTagChangeStatus status,
        string reviewNote, string? reversionNote)
    {
        await using var database = fixture.CreateContext();
        var persisted = await database.ImageTagChanges.SingleAsync(row => row.Id == change.Id);
        Assert.Equal(status, persisted.Status);
        Assert.Equal(reviewNote, persisted.ReviewNote);
        Assert.Equal(reversionNote, persisted.ReversionNote);
        var tags = await database.Images.Where(image => image.Id == change.ImageId)
            .SelectMany(image => image.Tags.Select(tag => tag.NormalizedName)).ToListAsync();
        Assert.Equal(status == ImageTagChangeStatus.Approved ? change.ProposedTags : change.PreviousTags, tags);
    }
}
