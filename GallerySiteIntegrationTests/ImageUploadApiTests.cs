using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Entities.Models;
using Microsoft.EntityFrameworkCore;
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
}
