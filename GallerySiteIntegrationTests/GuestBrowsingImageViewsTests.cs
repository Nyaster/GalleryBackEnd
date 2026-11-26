using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Entities.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace GallerySiteIntegrationTests;

public sealed partial class GuestBrowsingApiTests
{
    [Fact]
    public async Task ImageViews_UseDetailVisibilityAndRejectInvalidCredentials()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        var visitorAccount = await database.Authentication(context, seed.Clock).RegisterAsync(
            new($"viewsvisitor{Guid.NewGuid():N}", "integration-password"));
        var hiddenId = await context.Images.Where(image => image.DeletedAtUtc != null)
            .Select(image => image.Id).SingleAsync();
        var privateId = await context.Images.Where(image => image.Visibility == ImageVisibility.Private)
            .Select(image => image.Id).SingleAsync();
        var recorder = new ViewRecorder();
        await using var app = new ApiFactory(database, seed.Clock).WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IImageViewRecorder>();
                services.AddSingleton<IImageViewRecorder>(recorder);
            }));
        var options = new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://gallery.test"), HandleCookies = false, AllowAutoRedirect = false };
        using var guest = app.CreateClient(options);
        using var visitor = app.CreateClient(options);
        using var owner = app.CreateClient(options);
        using var invalid = app.CreateClient(options);
        visitor.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", visitorAccount.Response.AccessToken);
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.Token);
        invalid.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");

        var publicId = seed.EligibleIds[0];
        await AssertViewAccessAsync(invalid, publicId, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized);
        foreach (var deniedId in seed.DeniedIds.Append(int.MaxValue))
            await AssertViewAccessAsync(guest, deniedId, HttpStatusCode.NotFound, HttpStatusCode.NotFound);
        await AssertViewAccessAsync(visitor, privateId, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden);
        await AssertViewAccessAsync(visitor, hiddenId, HttpStatusCode.NotFound, HttpStatusCode.NotFound);
        await AssertViewAccessAsync(owner, hiddenId, HttpStatusCode.NoContent, HttpStatusCode.OK);
        await AssertViewAccessAsync(owner, privateId, HttpStatusCode.NoContent, HttpStatusCode.OK);
        await AssertViewAccessAsync(owner, publicId, HttpStatusCode.NoContent, HttpStatusCode.OK);
        Assert.Empty(recorder.Views);

        var detail = await ReadAsync<AppImageDto>(guest, $"/api/images/{publicId}");
        Assert.Equal(0L, detail.TotalViews);
        Assert.Equal(0L, detail.UniqueRegisteredViewers);
        await ReadAsync<List<ImageViewDay>>(guest, $"/api/images/{publicId}/views/daily");
        Assert.Empty(recorder.Views);

        await AssertViewAccessAsync(guest, publicId, HttpStatusCode.NoContent, HttpStatusCode.OK);
        await AssertViewAccessAsync(visitor, publicId, HttpStatusCode.NoContent, HttpStatusCode.OK);
        Assert.Collection(recorder.Views,
            observation => Assert.Equal((publicId, (int?)null), observation),
            observation => Assert.Equal((publicId, (int?)visitorAccount.Response.User.Id), observation));
    }

    [Fact]
    public async Task ImageViewDays_UseInclusiveUtcDatesAndRejectInvalidRanges()
    {
        var seed = await SeedAsync();
        await using var app = new ApiFactory(database, seed.Clock);
        using var client = app.Client();
        var path = $"/api/images/{seed.EligibleIds[0]}/views/daily";
        var today = DateOnly.FromDateTime(seed.Clock.GetUtcNow().UtcDateTime);
        var defaultDays = await ReadAsync<List<ImageViewDay>>(client, path);
        Assert.Equal(Enumerable.Range(0, 30).Select(offset => new ImageViewDay(today.AddDays(offset - 29), 0)),
            defaultDays);

        var fullYear = await ReadAsync<List<ImageViewDay>>(client, path + "?from=2024-01-01&through=2024-12-31");
        Assert.Equal(366, fullYear.Count);
        Assert.Equal(new ImageViewDay(new DateOnly(2024, 2, 29), 0), fullYear[59]);
        var last = await ReadAsync<List<ImageViewDay>>(client, path + "?from=9999-12-31&through=9999-12-31");
        Assert.Equal(new ImageViewDay(DateOnly.MaxValue, 0), Assert.Single(last));

        foreach (var query in new[]
                 {
                     "from=2024-01-01", "through=2024-01-01", "from=&through=", "from=2024-1-1&through=2024-01-01",
                     "from=2024-02-30&through=2024-03-01", "from=2024-02-02&through=2024-02-01",
                     "from=2024-01-01&through=2025-01-01", "from=0000-01-01&through=0001-01-01",
                     "from=2024-01-01&from=2024-01-02&through=2024-01-03"
                 })
        {
            using var response = await client.GetAsync($"{path}?{query}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    private static async Task AssertViewAccessAsync(HttpClient client, int imageId, HttpStatusCode writeStatus,
        HttpStatusCode readStatus)
    {
        using var write = await client.PostAsync($"/api/images/{imageId}/views", null);
        Assert.Equal(writeStatus, write.StatusCode);
        Assert.True(write.Headers.CacheControl?.NoStore);
        if (writeStatus == HttpStatusCode.NoContent)
            Assert.Empty(await write.Content.ReadAsByteArrayAsync());

        using var read = await client.GetAsync($"/api/images/{imageId}/views/daily");
        Assert.Equal(readStatus, read.StatusCode);
        Assert.True(read.Headers.CacheControl?.NoStore);
        Assert.Contains("Authorization", read.Headers.Vary);
    }

    private sealed class ViewRecorder : IImageViewRecorder
    {
        public ConcurrentQueue<(int ImageId, int? UserId)> Views { get; } = new();

        public void Record(int imageId, int? userId) => Views.Enqueue((imageId, userId));
    }
}