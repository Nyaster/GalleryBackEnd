using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.BackgroundService;
using Entities.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shared.DataTransferObjects;

namespace GallerySiteIntegrationTests;

public sealed class ImageViewFlowTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task ReportedOpensSurviveGracefulShutdownAndAreVisibleToGuests()
    {
        var clock = new TestClock();
        await using var database = fixture.CreateContext();
        var auth = fixture.Authentication(database, clock);
        var owner = (await auth.RegisterAsync(new("imageowner", "strong-test-password"))).Response;
        var viewer = (await auth.RegisterAsync(new("imageviewer", "strong-test-password"))).Response;
        var image = new UserMadeImage
        {
            UploadedById = owner.User.Id, Source = ImageSource.UserUpload, Visibility = ImageVisibility.Gallery,
            ModerationStatus = ModerationStatus.Approved, UploadedAtUtc = clock.GetUtcNow(),
            StorageKey = "uploads/public.jpg", ContentType = "image/jpeg",
            Tags = [new ImageTag { Name = "sfw", NormalizedName = "sfw", ModerationStatus = TagModerationStatus.Approved }]
        };
        database.Images.Add(image);
        await database.SaveChangesAsync();
        var path = $"/api/images/{image.Id}";
        await using var factory = new ApiFactory(fixture, clock);
        // ApiFactory disables background workers by default. Exercise the production flush on host shutdown.
        await using (var writer = factory.WithWebHostBuilder(builder => builder.ConfigureServices(
                         services => services.AddHostedService<ImageViewFlushService>())))
        {
            using var client = writer.CreateClient(new WebApplicationFactoryClientOptions
                { BaseAddress = new Uri("https://gallery.test") });
            var start = await client.GetFromJsonAsync<JsonElement>(path);
            Assert.Equal(0L, start.GetProperty("totalViews").GetInt64());
            Assert.Equal(0L, start.GetProperty("uniqueRegisteredViewers").GetInt64());
            foreach (var token in new[] { null, viewer.AccessToken, owner.AccessToken, viewer.AccessToken, null })
            {
                using var open = new HttpRequestMessage(HttpMethod.Post, $"{path}/views");
                if (token is not null) open.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var result = await client.SendAsync(open);
                Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
            }
        }

        using var reader = factory.Client();
        var detail = await reader.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(4L, detail.GetProperty("totalViews").GetInt64());
        Assert.Equal(1L, detail.GetProperty("uniqueRegisteredViewers").GetInt64());
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var days = await reader.GetFromJsonAsync<ImageViewDay[]>($"{path}/views/daily");
        Assert.NotNull(days);
        Assert.Equal(30, days.Length);
        Assert.Equal(today.AddDays(-29), days[0].Date);
        Assert.All(days[..^1], day => Assert.Equal(0L, day.NewUniqueViewers));
        Assert.Equal(new ImageViewDay(today, 1), days[^1]);
    }
}
