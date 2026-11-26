using Contracts;
using Entities.Models;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shared.DataTransferObjects;

namespace GallerySiteIntegrationTests;

public sealed class ImageViewRepositoryTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task ConcurrentBatchesAndRetries_IncrementTotalsOnceAndUseEarliestUtcDay()
    {
        await using var context = database.CreateContext();
        var owner = AddUser(context);
        var viewer = AddUser(context);
        var otherViewer = AddUser(context);
        var image = AddImage(context, owner);
        await context.SaveChangesAsync();

        var earlier = new DateTimeOffset(2026, 11, 10, 23, 59, 59, TimeSpan.Zero);
        var later = earlier.AddSeconds(1);
        var created = DateTimeOffset.UtcNow;
        var laterBatch = new ImageViewBatch(Guid.NewGuid(), created,
            new Dictionary<int, long> { [image.Id] = (long)int.MaxValue + 1 },
            new Dictionary<(int, int), DateTimeOffset> { [(image.Id, viewer.Id)] = later });
        var earlierBatch = new ImageViewBatch(Guid.NewGuid(), created,
            new Dictionary<int, long> { [image.Id] = 3 },
            new Dictionary<(int, int), DateTimeOffset>
            {
                [(image.Id, viewer.Id)] = earlier,
                [(image.Id, otherViewer.Id)] = later,
                [(image.Id, owner.Id)] = earlier,
                [(image.Id, int.MaxValue)] = earlier
            });

        await Apply(laterBatch);
        await Task.WhenAll(Apply(earlierBatch), Apply(earlierBatch), Apply(laterBatch),
            Apply(laterBatch with { Id = Guid.NewGuid() }));

        // Deliberately use a database session time zone in which both observations are on the same local date.
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("SET TIME ZONE 'America/Los_Angeles'");
        var repository = new ImageViewRepository(context);
        Assert.Equal(new ImageViewTotals(2L * (int.MaxValue + 1L) + 3, 2),
            await repository.GetTotalsAsync(image.Id, default));
        Assert.Equal(new[] { new ImageViewDay(new DateOnly(2026, 11, 10), 1), new ImageViewDay(new DateOnly(2026, 11, 11), 1) },
            await repository.GetDailyAsync(image.Id, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 11), default));
        Assert.Equal(new[] { new ImageViewDay(new DateOnly(2026, 11, 11), 1) },
            await repository.GetDailyAsync(image.Id, new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 11), default));
    }

    [Fact]
    public async Task MissingRecordsAndExpiredBatches_AreSkippedAndKeepPreviouslyRecordedHistory()
    {
        await using var context = database.CreateContext();
        var owner = AddUser(context);
        var viewer = AddUser(context);
        var deletedViewer = AddUser(context);
        var image = AddImage(context, owner);
        var deletedImage = AddImage(context, owner);
        await context.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var date = DateOnly.FromDateTime(now.UtcDateTime);
        var repository = new ImageViewRepository(context);
        Assert.Equal(new ImageViewTotals(0, 0), await repository.GetTotalsAsync(image.Id, default));
        await Apply(new ImageViewBatch(Guid.NewGuid(), now, new Dictionary<int, long> { [image.Id] = 1 },
            new Dictionary<(int, int), DateTimeOffset> { [(image.Id, viewer.Id)] = now }));

        context.AppUsers.RemoveRange(viewer, deletedViewer);
        context.Images.Remove(deletedImage);
        image.DeletedAtUtc = now;
        await context.SaveChangesAsync();

        var totals = new Dictionary<int, long> { [image.Id] = 1, [deletedImage.Id] = 1 };
        var observations = new Dictionary<(int, int), DateTimeOffset>
        {
            [(image.Id, deletedViewer.Id)] = now,
            [(deletedImage.Id, owner.Id)] = now
        };
        var expired = new ImageViewBatch(Guid.NewGuid(), now.AddDays(-2), totals, observations);
        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO "ImageViewBatchReceipts" ("Id", "CreatedAtUtc")
            VALUES ({expired.Id}, {expired.CreatedAtUtc})
            """);
        await Apply(expired);
        await Apply(new ImageViewBatch(Guid.NewGuid(), now, totals, observations));

        Assert.Equal(new ImageViewTotals(2, 1), await repository.GetTotalsAsync(image.Id, default));
        Assert.Equal(new ImageViewTotals(0, 0), await repository.GetTotalsAsync(deletedImage.Id, default));
        Assert.Equal(new[] { new ImageViewDay(date, 1) }, await repository.GetDailyAsync(image.Id, date, date, default));
        Assert.False(await context.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM "ImageViewBatchReceipts" WHERE "Id" = {expired.Id}) AS "Value"
            """).SingleAsync());
    }

    [Fact]
    public async Task ConcurrentNonKeyImageUpdate_DoesNotBlockBatch()
    {
        await using var context = database.CreateContext();
        var owner = AddUser(context);
        var viewer = AddUser(context);
        var image = AddImage(context, owner);
        await context.SaveChangesAsync();

        await using var updating = database.CreateContext();
        await using var transaction = await updating.Database.BeginTransactionAsync();
        await updating.Images.Where(candidate => candidate.Id == image.Id)
            .ExecuteUpdateAsync(imageSet => imageSet.SetProperty(candidate => candidate.Width, 500));

        var now = DateTimeOffset.UtcNow;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Apply(new ImageViewBatch(Guid.NewGuid(), now, new Dictionary<int, long> { [image.Id] = 1 },
            new Dictionary<(int, int), DateTimeOffset> { [(image.Id, viewer.Id)] = now }), timeout.Token);
        Assert.Equal(new ImageViewTotals(1, 1), await new ImageViewRepository(context).GetTotalsAsync(image.Id, default));
    }

    private async Task Apply(ImageViewBatch batch, CancellationToken cancellationToken = default)
    {
        await using var context = database.CreateContext();
        await new ImageViewRepository(context).ApplyAsync(batch, cancellationToken);
    }

    private static AppUser AddUser(Repository.RepositoryContext context)
    {
        var login = Guid.NewGuid().ToString("N");
        return context.AppUsers.Add(new AppUser
        {
            Login = login, NormalizedLogin = login.ToUpperInvariant(), PasswordHash = "unused",
            CreatedAtUtc = DateTimeOffset.UtcNow
        }).Entity;
    }

    private static UserMadeImage AddImage(Repository.RepositoryContext context, AppUser owner)
        => context.UserMadeImages.Add(new UserMadeImage
        {
            StorageKey = Guid.NewGuid().ToString("N"), ContentType = "image/png", UploadedBy = owner,
            UploadedAtUtc = DateTimeOffset.UtcNow, Visibility = ImageVisibility.Gallery,
            ModerationStatus = ModerationStatus.Approved, Width = 100, Height = 100
        }).Entity;
}
