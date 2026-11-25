using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Entities.Models;
using Microsoft.EntityFrameworkCore;

namespace GallerySiteIntegrationTests;

public sealed class ImageModerationApiTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData(ImageVisibility.Private)]
    [InlineData(ImageVisibility.Gallery)]
    public async Task ApprovingAnotherUploadReturnsOnlyWhatRemainsReadable(ImageVisibility visibility)
    {
        await using var api = await RoleAssignmentTestApi.StartAsync(fixture);
        var image = await SeedPendingImageAsync(api.MemberId, api.ManagerId, visibility);
        var url = $"/api/admin/images/{image.Id}/moderation";

        using var response = await api.Member.PatchAsJsonAsync(url, new { status = "Approved", expectedStatus = "Pending" });
        using var detail = await api.Member.GetAsync($"/api/images/{image.Id}");

        if (visibility == ImageVisibility.Private)
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync());
            Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var card = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal($"/api/images/{image.Id}/content", card.GetProperty("contentUrl").GetString());
            Assert.False(card.TryGetProperty("pendingTags", out var pending) && pending.ValueKind == JsonValueKind.Array &&
                         pending.GetArrayLength() > 0);
        }

        await using var database = fixture.CreateContext();
        Assert.Equal(ModerationStatus.Approved, await database.Images.Where(row => row.Id == image.Id)
            .Select(row => row.ModerationStatus).SingleAsync());
    }

    [Fact]
    public async Task OwnerClassificationKeepsOrdinaryContentUrlAndStatusDecisionsRequireFreshBaseline()
    {
        await using var api = await RoleAssignmentTestApi.StartAsync(fixture);
        var image = await SeedPendingImageAsync(api.MemberId, api.MemberId, ImageVisibility.Private);
        var url = $"/api/admin/images/{image.Id}/moderation";

        using var classification = await api.Member.PatchAsJsonAsync(url, new { aiUsage = "AiAssisted" });
        Assert.Equal(HttpStatusCode.OK, classification.StatusCode);
        var classified = await classification.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal($"/api/images/{image.Id}/content", classified.GetProperty("contentUrl").GetString());
        Assert.Equal("Pending", classified.GetProperty("moderationStatus").GetString());

        using var missing = await api.Member.PatchAsJsonAsync(url, new { status = "Approved" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using var accepted = await api.Member.PatchAsJsonAsync(url, new { status = "Approved", expectedStatus = "Pending" });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var stale = await api.Member.PatchAsJsonAsync(url, new { status = "Rejected", expectedStatus = "Pending" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var current = await api.Member.GetFromJsonAsync<JsonElement>($"/api/images/{image.Id}");
        Assert.Equal("Approved", current.GetProperty("moderationStatus").GetString());
        Assert.Equal("AiAssisted", current.GetProperty("aiUsage").GetString());
    }

    private async Task<UserMadeImage> SeedPendingImageAsync(int reviewerId, int ownerId, ImageVisibility visibility)
    {
        await using var database = fixture.CreateContext();
        var name = $"review-{reviewerId}";
        var role = new AccessRole
        {
            Name = name, NormalizedName = name.ToUpperInvariant(),
            Permissions = [
                new() { PermissionKey = AccessPermission.ImagesReviewQueue },
                new() { PermissionKey = AccessPermission.ImagesModerate },
                new() { PermissionKey = AccessPermission.ImagesClassifyAi }
            ]
        };
        database.AccountRoles.Add(new AccountRole { UserId = reviewerId, Role = role });
        var image = new UserMadeImage
        {
            StorageKey = $"uploads/{name}.jpg", ContentType = "image/jpeg", UploadedById = ownerId,
            UploadedAtUtc = DateTimeOffset.UtcNow, Visibility = visibility, ModerationStatus = ModerationStatus.Pending,
            Tags = [new ImageTag { Name = name, NormalizedName = name, ModerationStatus = TagModerationStatus.Pending }]
        };
        database.Images.Add(image);
        await database.SaveChangesAsync();
        return image;
    }
}
