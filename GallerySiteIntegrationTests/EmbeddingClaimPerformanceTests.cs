using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pgvector;
using Repository;
using Service;
using Service.Contracts;
using Xunit.Abstractions;
using static GallerySiteIntegrationTests.ImageRepositoryPerformanceSupport;

namespace GallerySiteIntegrationTests;

[Collection("Image repository performance")]
public sealed class EmbeddingClaimPerformanceTests(PostgresFixture database, ITestOutputHelper output)
    : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task ClaimPendingEmbeddings_EligibleStates_UpdatesLeasesAndReturnsOnlyIds()
    {
        await ResetAsync(database);
        int[] ids;
        await using (var seed = database.CreateContext())
        {
            var images = Enumerable.Range(0, 8).Select(Image).ToArray();
            images[0].EmbeddingAttempts = 2;
            images[1].EmbeddingStatus = EmbeddingStatus.Processing;
            images[1].EmbeddingLeaseExpiresAtUtc = Now.AddTicks(-10);
            images[2].EmbeddingStatus = EmbeddingStatus.Processing;
            images[2].EmbeddingLeaseExpiresAtUtc = Now;
            images[3].EmbeddingStatus = EmbeddingStatus.Processing;
            images[3].EmbeddingLeaseExpiresAtUtc = Now.AddMinutes(1);
            images[4].EmbeddingStatus = EmbeddingStatus.Processing;
            images[4].EmbeddingLeaseExpiresAtUtc = null;
            images[5].EmbeddingStatus = EmbeddingStatus.Ready;
            images[6].EmbeddingStatus = EmbeddingStatus.Failed;
            images[7].DeletedAtUtc = Now;
            seed.Images.AddRange(images);
            await seed.SaveChangesAsync();
            ids = images.Select(image => image.Id).ToArray();
        }
        var capture = new QueryCapture();
        var metrics = new ImageRepositoryReadMetrics();
        await using var context = database.CreateContext(capture, metrics);
        var claimed = await new AppImageRepository(context).ClaimPendingEmbeddingsAsync(50, Now);

        Assert.Equal(ids.Take(2).Order(), claimed.Order());
        Assert.Empty(capture.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Single(capture.Reads);
        Assert.Contains("FOR UPDATE SKIP LOCKED", capture.Reads[0]);
        Assert.DoesNotContain("RETURNING *", capture.Reads[0]);
        Assert.Equal(1, Assert.Single(metrics.Reads).Columns);
        Assert.Equal(2, metrics.Reads[0].Rows);
        await using var verify = database.CreateContext();
        var rows = await verify.Images.OrderBy(image => image.Id).ToListAsync();
        Assert.Equal(3, rows[0].EmbeddingAttempts);
        Assert.Equal(1, rows[1].EmbeddingAttempts);
        Assert.All(rows.Take(2), image =>
        {
            Assert.Equal(EmbeddingStatus.Processing, image.EmbeddingStatus);
            Assert.Equal(Now.AddMinutes(15), image.EmbeddingLeaseExpiresAtUtc);
            Assert.Null(image.EmbeddingError);
        });
        Assert.All(rows.Skip(2), image =>
        {
            Assert.Equal(0, image.EmbeddingAttempts);
            Assert.Equal("Previous attempt failed", image.EmbeddingError);
        });
    }

    [Fact]
    public async Task ClaimPendingEmbeddings_ConcurrentTransactions_SkipLockedRowsAndClaimDisjointBatches()
    {
        await SeedAsync(6);
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        await using var transaction = await first.Database.BeginTransactionAsync();
        var firstIds = await new AppImageRepository(first).ClaimPendingEmbeddingsAsync(3, Now);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        // The first transaction still holds row locks, forcing the second worker to skip them.
        var secondIds = await new AppImageRepository(second).ClaimPendingEmbeddingsAsync(3, Now, timeout.Token);
        await transaction.CommitAsync();

        Assert.Equal(new[] { 1, 2, 3 }, firstIds.Order());
        Assert.Equal(3, secondIds.Count);
        Assert.Empty(firstIds.Intersect(secondIds));
        Assert.Equal(6, firstIds.Concat(secondIds).Distinct().Count());
        Assert.Empty(first.ChangeTracker.Entries());
        Assert.Empty(second.ChangeTracker.Entries());
        Assert.Empty(await new AppImageRepository(second).ClaimPendingEmbeddingsAsync(3, Now));
    }

    [Fact]
    public async Task ClaimPendingEmbeddings_CanceledToken_DoesNotClaimRows()
    {
        await SeedAsync(2);
        await using var context = database.CreateContext();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AppImageRepository(context).ClaimPendingEmbeddingsAsync(2, Now, cancellation.Token));

        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.Images.CountAsync(image => image.EmbeddingStatus == EmbeddingStatus.Pending));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ClaimPendingEmbeddings_BatchBounds_ClampsToOneAndFifty()
    {
        await SeedAsync(60);
        await using var context = database.CreateContext();
        var repository = new AppImageRepository(context);
        Assert.Single(await repository.ClaimPendingEmbeddingsAsync(int.MinValue, Now));
        Assert.Equal(50, (await repository.ClaimPendingEmbeddingsAsync(int.MaxValue, Now)).Count);
        Assert.Equal(9, (await repository.ClaimPendingEmbeddingsAsync(50, Now)).Count);
    }

    [Fact]
    public async Task ClaimPendingEmbeddings_SameContextHandler_ReloadsAndPersistsReadyState()
    {
        await SeedAsync(1);
        await using var context = database.CreateContext();
        var repositories = new RepositoryManager(context);
        var id = Assert.Single(await repositories.AppImage.ClaimPendingEmbeddingsAsync(1, Now));
        Assert.Empty(context.ChangeTracker.Entries());
        var handler = new Application.Features.Images.GenerateImageEmbedding.Handler(repositories,
            new EmbeddingStorage(), new EmbeddingGenerator(), Options.Create(new EmbeddingOptions()),
            NullLogger<Application.Features.Images.GenerateImageEmbedding.Handler>.Instance);

        await handler.Handle(new(id), CancellationToken.None);

        await using var verify = database.CreateContext();
        var image = await verify.Images.SingleAsync();
        Assert.Equal(EmbeddingStatus.Ready, image.EmbeddingStatus);
        Assert.Equal(1, image.EmbeddingAttempts);
        Assert.Null(image.EmbeddingLeaseExpiresAtUtc);
        Assert.Null(image.EmbeddingError);
        Assert.All(image.Embedding!.ToArray(), value => Assert.Equal(0.25f, value));
    }

    [ImageRepositoryPerformanceFact]
    public async Task ClaimPendingEmbeddings_SyntheticDataset_ReportsBeforeAndAfter()
    {
        await SeedAsync(50);
        output.WriteLine("claims dataset: 50 pending images with null embeddings; batch size=50");
        foreach (var baseline in new[] { true, false })
        {
            await ResetClaimsAsync();
            var capture = new QueryCapture();
            var metrics = new ImageRepositoryReadMetrics();
            await using (var context = database.CreateContext(capture, metrics))
            {
                var ids = await ClaimAsync(context, baseline);
                Assert.Equal(50, ids.Count);
                output.WriteLine($"claims {(baseline ? "before" : "after")}: entities={capture.Entities.Count} " +
                    $"tracked={context.ChangeTracker.Entries().Count()} " +
                    $"rows=[{string.Join(',', metrics.Reads.Select(read => read.Rows))}] " +
                    $"columns=[{string.Join(',', metrics.Reads.Select(read => read.Columns))}]");
            }
            await MeasureAsync($"claims {(baseline ? "before" : "after")}", async () =>
            {
                await using var context = database.CreateContext();
                await ClaimAsync(context, baseline);
            }, ResetClaimsAsync, output);
        }
    }

    private static async Task<List<int>> ClaimAsync(RepositoryContext context, bool baseline)
    {
        if (!baseline) return await new AppImageRepository(context).ClaimPendingEmbeddingsAsync(50, Now);
        var leaseExpires = Now.AddMinutes(15);
        // Exact original synchronous, tracked full-entity UPDATE RETURNING baseline.
        var images = context.Images.FromSqlInterpolated($"""
            UPDATE "Images" SET "EmbeddingStatus" = {"Processing"}, "EmbeddingAttempts" = "EmbeddingAttempts" + 1,
                "EmbeddingError" = NULL, "EmbeddingLeaseExpiresAtUtc" = {leaseExpires}
            WHERE "Id" IN (
                SELECT "Id" FROM "Images"
                WHERE "DeletedAtUtc" IS NULL AND ("EmbeddingStatus" = {"Pending"} OR
                    ("EmbeddingStatus" = {"Processing"} AND "EmbeddingLeaseExpiresAtUtc" < {Now}))
                ORDER BY "UploadedAtUtc" FOR UPDATE SKIP LOCKED LIMIT {50}
            ) RETURNING *
            """).AsEnumerable().ToList();
        return images.Select(image => image.Id).ToList();
    }

    private async Task SeedAsync(int count)
    {
        await ResetAsync(database);
        await using var context = database.CreateContext();
        context.Images.AddRange(Enumerable.Range(0, count).Select(index =>
        {
            var image = Image(index);
            image.Embedding = null;
            return image;
        }));
        await context.SaveChangesAsync();
    }

    private async Task ResetClaimsAsync()
    {
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlRawAsync("""
            UPDATE "Images" SET "EmbeddingStatus" = 'Pending', "EmbeddingAttempts" = 0,
                "EmbeddingError" = 'Previous attempt failed', "EmbeddingLeaseExpiresAtUtc" = NULL
            """);
    }

    private sealed class EmbeddingGenerator : IImageEmbeddingGenerator
    {
        public Task<Vector> GenerateEmbeddingAsync(Stream imageStream, CancellationToken cancellationToken)
            => Task.FromResult(new Vector(Enumerable.Repeat(0.25f, 1024).ToArray()));
    }

    private sealed class EmbeddingStorage : IImageStorage
    {
        public bool Exists(string storageKey) => true;
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
        public Task<string> SaveTemporaryAsync(Stream source, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task DeleteTemporaryAsync(string temporaryPath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task MoveTemporaryToFinalAsync(string temporaryPath, string storageKey, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
