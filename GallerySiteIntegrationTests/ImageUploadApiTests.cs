using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Shared.DataTransferObjects;
using SkiaSharp;

namespace GallerySiteIntegrationTests;

public sealed class ImageUploadApiTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Upload_WithRepeatedTagFields_PersistsExistingAndNewTags()
    {
        var clock = new TestClock();
        await using var context = database.CreateContext();
        var session = await database.Authentication(context, clock)
            .RegisterAsync(new("uploader", "strong-test-password"));
        await context.AppUsers.Where(user => user.Id == session.Response.User.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.CanUploadImages, true));
        context.Tags.Add(new ImageTag
        {
            Name = "portrait", NormalizedName = "portrait", CreatedAtUtc = clock.GetUtcNow(),
            ModerationStatus = TagModerationStatus.Approved
        });
        await context.SaveChangesAsync();

        await using var app = new ApiFactory(database, clock);
        using var client = app.Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Response.AccessToken);
        using var bitmap = new SKBitmap(1, 1);
        bitmap.Erase(SKColors.Blue);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(png.ToArray()), "imageFile", "image.png" },
            { new StringContent("HumanMade"), "aiUsage" },
            { new StringContent("portrait"), "tags" },
            { new StringContent("summer"), "tags" }
        };

        using var response = await client.PostAsync("/api/images", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var image = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = image.GetProperty("id").GetInt32();
        var savedTags = await context.Images.Where(row => row.Id == id)
            .SelectMany(row => row.Tags).OrderBy(tag => tag.Name).Select(tag => tag.Name).ToListAsync();
        Assert.Equal(["portrait", "summer"], savedTags);
        Assert.Equal("portrait", image.GetProperty("tags").EnumerateArray().Single().GetString());
        Assert.Equal("summer", image.GetProperty("pendingTags").EnumerateArray().Single().GetString());
    }

    [Fact]
    public async Task ExistingSession_UsesTrustedContributorGrantsAndRevocationsOnNewSubmissions()
    {
        var clock = new TestClock();
        await using var context = database.CreateContext();
        var auth = database.Authentication(context, clock);
        var owner = await auth.RegisterAsync(new("trustedowner", "strong-test-password"));
        var admin = await auth.RegisterAsync(new("trustedadmin", "strong-test-password"));
        context.AccountRoles.Add(new AccountRole { UserId = admin.Response.User.Id, RoleId = AccessRole.AdminId });
        await context.SaveChangesAsync();

        await using var app = new ApiFactory(database, clock);
        using var contributor = app.Client();
        contributor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.Response.AccessToken);
        using var manager = app.Client();
        manager.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Response.AccessToken);
        var accountPath = $"/api/admin/access/accounts/{owner.Response.User.Id}/roles";
        (await manager.PutAsJsonAsync(accountPath, new { roleIds = new[] { AccessRole.TrustedContributorId } }))
            .EnsureSuccessStatusCode();
        var roles = await manager.GetFromJsonAsync<AccessRoleDto[]>("/api/admin/access/roles");
        Assert.Equal(1, roles!.Single(role => role.Id == AccessRole.TrustedContributorId).MemberCount);
        var members = await manager.GetFromJsonAsync<AccessAccountDto[]>(
            $"/api/admin/access/roles/{AccessRole.TrustedContributorId}/members?page=1&pageSize=1");
        Assert.Equal(owner.Response.User.Id, Assert.Single(members!).Id);

        var profile = await contributor.GetFromJsonAsync<AppUserDto>("/api/auth/me");
        Assert.Contains("Trusted Contributor", profile!.Roles);
        var inspected = await manager.GetFromJsonAsync<AccountAccessDto>(
            $"/api/admin/access/accounts/{owner.Response.User.Id}/permissions");
        Assert.Equal(profile.Permissions.ToArray(), inspected!.Permissions.ToArray());
        Assert.Equal([AccessPermission.ImageTagsBypassReviewOnOwnImages, AccessPermission.UploadsBypassReview],
            profile.Permissions.Order(StringComparer.Ordinal));
        Assert.False(profile.CanUploadImages);
        using var denied = await UploadPrivateImage(contributor);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        (await manager.PutAsJsonAsync($"/api/admin/users/{owner.Response.User.Id}/upload-permission",
            new { canUploadImages = true })).EnsureSuccessStatusCode();
        using var accepted = await UploadPrivateImage(contributor);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        var image = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Approved", image.GetProperty("moderationStatus").GetString());
        Assert.Equal("Private", image.GetProperty("visibility").GetString());
        var tagsPath = $"/api/images/{image.GetProperty("id").GetInt32()}/tags";
        using var edit = await contributor.PutAsJsonAsync(tagsPath, new { tags = new[] { "trustedtag" } });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var applied = await edit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Approved", applied.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, applied.GetProperty("reviewedByUserId").ValueKind);
        Assert.Equal(TagModerationStatus.Pending, (await context.Tags.SingleAsync(t => t.Name == "trustedtag")).ModerationStatus);

        (await manager.PutAsJsonAsync(accountPath, new { roleIds = Array.Empty<int>() })).EnsureSuccessStatusCode();
        using var proposal = await contributor.PutAsJsonAsync(tagsPath, new { tags = new[] { "afterrevocation" } });
        Assert.Equal(HttpStatusCode.Accepted, proposal.StatusCode);
        using var ordinary = await UploadPrivateImage(contributor);
        Assert.Equal(HttpStatusCode.Created, ordinary.StatusCode);
        Assert.Equal("Pending", (await ordinary.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("moderationStatus").GetString());
    }

    private static async Task<HttpResponseMessage> UploadPrivateImage(HttpClient client)
    {
        using var bitmap = new SKBitmap(1, 1);
        bitmap.Erase(SKColors.Blue);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(png.ToArray()), "imageFile", "private.png" },
            { new StringContent("HumanMade"), "aiUsage" },
            { new StringContent("true"), "isPrivate" }
        };
        return await client.PostAsync("/api/images", form);
    }
}
