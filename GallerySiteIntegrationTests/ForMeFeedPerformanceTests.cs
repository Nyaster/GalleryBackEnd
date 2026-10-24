using System.Data.Common;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Contracts;
using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using NpgsqlTypes;
using Pgvector;
using Pgvector.Npgsql;
using Repository;
using Xunit.Abstractions;

namespace GallerySiteIntegrationTests;

[Collection("Image repository performance")]
public sealed class ForMeFeedPerformanceTests(PostgresFixture database, ITestOutputHelper output)
    : IClassFixture<PostgresFixture>
{
    [ImageRepositoryPerformanceFact]
    public async Task Feed_HundredThousandDenseVectors_MeetsGenerationAndPagingBudgets()
    {
        var clock = new TestClock();
        output.WriteLine($"OS={RuntimeInformation.OSDescription}; CPU={Environment.ProcessorCount}; " +
                         $"framework={RuntimeInformation.FrameworkDescription}; configuration=" +
#if DEBUG
                         "Debug");
#else
                         "Release");
#endif
        var users = await SeedAsync(clock);
        var generationP95 = new List<double>();
        foreach (var filters in new[] { new ForMeFilters([], []), new ForMeFilters([1], ["exclude-performance"]) })
        {
            async Task<double> GenerateAsync(int userId)
            {
                await using var context = database.CreateContext();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var watch = Stopwatch.StartNew();
                var id = await new ForMeFeedRepository(context, clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<ForMeFeedRepository>.Instance).CreateAsync(userId, 20, filters, deadline.Token);
                Assert.Equal(500, await context.ForMeFeedEntries.CountAsync(entry => entry.FeedId == id));
                return watch.Elapsed.TotalMilliseconds;
            }
            for (var warmup = 0; warmup < 3; warmup++)
                await Task.WhenAll(users.Take(2).Select(user => GenerateAsync(user.Id)));
            var times = new List<double>();
            for (var sample = 0; sample < 10; sample++)
                times.AddRange(await Task.WhenAll(users.Take(2).Select(user => GenerateAsync(user.Id))));
            var p95 = Percentile95(times);
            generationP95.Add(p95);
            output.WriteLine($"generation aiFilters={filters.AiUsage.Length} tagExclusions={filters.ExcludedTags.Length}: " +
                             $"warmups=3 concurrency=2 samples={times.Count} p95_ms={p95:F2} min_ms={times.Min():F2} max_ms={times.Max():F2}");
        }

        var capture = new PlanCapture();
        await using var planContext = database.CreateContext(capture);
        var feedId = await new ForMeFeedRepository(planContext, clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<ForMeFeedRepository>.Instance).CreateAsync(users[0].Id, 20, new([], []));
        Assert.NotNull(capture.Sql);
        await planContext.Database.OpenConnectionAsync();
        await using var explain = planContext.Database.GetDbConnection().CreateCommand();
        explain.CommandText = "EXPLAIN (ANALYZE, BUFFERS) " + capture.Sql;
        foreach (var parameter in capture.Parameters) explain.Parameters.Add(parameter.Clone());
        await using (var reader = await explain.ExecuteReaderAsync())
        {
            var plan = new List<string>();
            while (await reader.ReadAsync()) plan.Add(reader.GetString(0));
            var text = string.Join(Environment.NewLine, plan);
            output.WriteLine(text);
            Assert.DoesNotContain("IX_Images_Embedding", text);
            Assert.Contains("Sort", text);
        }

        async Task<double> PageAsync()
        {
            await using var context = database.CreateContext();
            var watch = Stopwatch.StartNew();
            var page = await new ForMeFeedRepository(context, clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<ForMeFeedRepository>.Instance).ReadAsync(feedId, new(users[0].Id, false), 20);
            Assert.Equal(20, page.Images.Count);
            return watch.Elapsed.TotalMilliseconds;
        }
        for (var warmup = 0; warmup < 3; warmup++)
            await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => PageAsync()));
        var pageTimes = new List<double>();
        for (var sample = 0; sample < 10; sample++)
            pageTimes.AddRange(await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => PageAsync())));
        var pageP95 = Percentile95(pageTimes);
        output.WriteLine($"page warmups=3 concurrency=10 samples={pageTimes.Count} p95_ms={pageP95:F2} " +
                         $"min_ms={pageTimes.Min():F2} max_ms={pageTimes.Max():F2}");
        Assert.All(generationP95, p95 => Assert.True(p95 <= 5000, $"Generation p95 {p95:F2}ms exceeds 5000ms."));
        Assert.True(pageP95 <= 300, $"Paging p95 {pageP95:F2}ms exceeds 300ms.");
    }

    private async Task<AppUser[]> SeedAsync(TimeProvider clock)
    {
        await using var context = database.CreateContext();
        var users = Enumerable.Range(0, 52).Select(index => new AppUser
        {
            Login = "performance" + index, NormalizedLogin = "PERFORMANCE" + index,
            PasswordHash = "unused", CreatedAtUtc = clock.GetUtcNow()
        }).ToArray();
        context.AppUsers.AddRange(users);
        var tag = new ImageTag
        {
            Name = "exclude-performance", NormalizedName = "exclude-performance",
            ModerationStatus = TagModerationStatus.Approved, CreatedAtUtc = clock.GetUtcNow()
        };
        context.Tags.Add(tag);
        await context.SaveChangesAsync();
        // Bulk build the disposable fixture's existing index after COPY, preserving the production schema for measurements.
        await context.Database.ExecuteSqlRawAsync("DROP INDEX \"IX_Images_Embedding\"");
        var sourceBuilder = new NpgsqlDataSourceBuilder(database.ConnectionString);
        sourceBuilder.UseVector();
        await using var source = sourceBuilder.Build();
        await using var connection = await source.OpenConnectionAsync();
        var random = new Random(314159);
        var centers = Enumerable.Range(0, 10).Select(_ => Enumerable.Range(0, 1024)
            .Select(_ => random.NextSingle() * 2 - 1).ToArray()).ToArray();
        await using (var copy = await connection.BeginBinaryImportAsync("""
            COPY "Images" ("Id", "Source", "UploadedById", "UploadedAtUtc", "Visibility", "ModerationStatus",
                "AiUsage", "StorageKey", "ContentType", "Width", "Height", "Embedding", "EmbeddingStatus",
                "EmbeddingAttempts", "image_type") FROM STDIN (FORMAT BINARY)
            """))
        {
            copy.Timeout = TimeSpan.FromMinutes(10);
            for (var index = 1; index <= 100_000; index++)
            {
                var vector = new float[1024];
                for (var dimension = 0; dimension < vector.Length; dimension++)
                    vector[dimension] = centers[index % 10][dimension] * 3 + random.NextSingle() * 0.4f - 0.2f;
                await copy.StartRowAsync();
                await copy.WriteAsync(index, NpgsqlDbType.Integer);
                await copy.WriteAsync("UserUpload", NpgsqlDbType.Text);
                await copy.WriteAsync(users[0].Id, NpgsqlDbType.Integer);
                await copy.WriteAsync(clock.GetUtcNow().AddSeconds(-index), NpgsqlDbType.TimestampTz);
                await copy.WriteAsync("Gallery", NpgsqlDbType.Text);
                await copy.WriteAsync("Approved", NpgsqlDbType.Text);
                await copy.WriteAsync(index % 4 == 0 ? "HumanMade" : "AiGenerated", NpgsqlDbType.Text);
                await copy.WriteAsync("performance/" + index, NpgsqlDbType.Varchar);
                await copy.WriteAsync("image/jpeg", NpgsqlDbType.Varchar);
                await copy.WriteAsync(1024, NpgsqlDbType.Integer);
                await copy.WriteAsync(1024, NpgsqlDbType.Integer);
                await copy.WriteAsync(new Vector(vector), "vector");
                await copy.WriteAsync("Ready", NpgsqlDbType.Text);
                await copy.WriteAsync(0, NpgsqlDbType.Integer);
                await copy.WriteAsync("user", NpgsqlDbType.Varchar);
            }
            await copy.CompleteAsync();
        }
        context.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        await context.Database.ExecuteSqlRawAsync("CREATE INDEX \"IX_Images_Embedding\" ON \"Images\" USING hnsw (\"Embedding\" vector_l2_ops) WHERE \"Embedding\" IS NOT NULL");
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AppImageImageTag" ("AppImagesId", "TagsId")
            SELECT "Id", {tag.Id} FROM "Images" WHERE "Id" % 3 = 0
            """);
        foreach (var user in users.Take(2))
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ImageLikes" ("ImageId", "UserId", "CreatedAtUtc")
                SELECT ((series * 9973) % 100000) + 1, {user.Id}, {clock.GetUtcNow()}
                FROM generate_series(1, 100) AS series
                """);
        foreach (var user in users.Skip(2))
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ImageLikes" ("ImageId", "UserId", "CreatedAtUtc")
                SELECT ((series * 7919 + {user.Id} * 17) % 100000) + 1, {user.Id}, {clock.GetUtcNow()}
                FROM generate_series(1, 1000) AS series
                """);
        await context.Database.ExecuteSqlRawAsync("ANALYZE");
        var vectors = await context.ImageLikes.Where(like => like.UserId == users[0].Id)
            .OrderByDescending(like => like.CreatedAtUtc).ThenByDescending(like => like.ImageId)
            .Select(like => like.Image!.Embedding!).ToListAsync();
        Assert.Equal(10, ForMeRanking.SelectRepresentatives(vectors.Select(vector => vector.ToArray()).ToArray()).Count);
        output.WriteLine("fixture=100000 images, dense1024D, ten visual clusters, currentLikes=50200, approvedTagLinks=33333; " +
                         "PostgreSQL17 pgvector/pgvector:pg17; production HNSW index present; unrestricted and ~17% selective filters");
        return users;
    }

    private static double Percentile95(IEnumerable<double> values)
    {
        var ordered = values.Order().ToArray();
        return ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1];
    }

    private sealed class PlanCapture : DbCommandInterceptor
    {
        public string? Sql { get; private set; }
        public List<NpgsqlParameter> Parameters { get; private set; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("+ 0.0"))
            {
                Sql = command.CommandText;
                Parameters = command.Parameters.Cast<NpgsqlParameter>().Select(parameter => parameter.Clone()).ToList();
            }
            return ValueTask.FromResult(result);
        }
    }
}
