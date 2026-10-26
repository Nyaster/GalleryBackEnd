using System.Data;
using System.Data.Common;
using Contracts;
using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using Repository;
using Shared.Configuration;

namespace GallerySiteIntegrationTests;

public sealed partial class ForMeVectorSearchTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData(25)]
    [InlineData(100)]
    public async Task Create_ConfiguredReferences_UsesEachDistinctInterestAndBoundedProjections(int count)
    {
        var user = await SeedAsync(count, 650);
        var capture = new QueryCapture();
        var metrics = new AnnReadMetrics();
        await using var context = database.CreateContext(capture, metrics);
        var id = await Repository(context, count).CreateAsync(user, 20, new([], []));
        var neighbors = capture.Reads.Where(sql => sql.Contains(ForMeFeedRepository.AnnQueryTag)).ToArray();
        Assert.Equal(count, neighbors.Length);
        Assert.All(neighbors, sql => Assert.DoesNotContain("+ 0.0", sql));
        Assert.Equal(count, metrics.Reads.Count);
        Assert.All(metrics.Reads, read =>
        {
            Assert.Equal(2, read.Columns);
            Assert.InRange(read.Rows, 1, ForMeFeedRepository.AnnCandidateLimit);
        });
        Assert.DoesNotContain(capture.Reads, sql => sql.Contains(ForMeFeedRepository.ExactTopUpQueryTag));
        Assert.DoesNotContain(capture.Entities, entity => entity is AppImage or ImageLike);
        var stored = await context.ForMeFeedEntries.Where(entry => entry.FeedId == id).Select(entry => entry.ImageId).ToListAsync();
        Assert.Equal(500, stored.Count);
        Assert.Equal(500, stored.Distinct().Count());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Create_CandidatePoolExceedsShortlist_BoundsMaterializedRows(bool usage, bool tag)
    {
        var filters = new ForMeFilters(usage ? [(int)AiUsageClassification.HumanMade] : [], tag ? ["nonexistent-bound-tag"] : []);
        var limit = ForMeFeedRepository.AnnCandidateLimit;
        Assert.Equal(500, limit);
        var user = await SeedAsync(1, limit + 250);
        var capture = new QueryCapture();
        var metrics = new AnnReadMetrics();
        await using var context = database.CreateContext(capture, metrics);
        var logger = new SearchLogger();
        await Repository(context, 1, logger).CreateAsync(user, 20, filters);
        Assert.Equal(limit, Assert.Single(logger.Events)["AnnCandidateLimit"]);
        var read = Assert.Single(metrics.Reads);
        Assert.Equal(2, read.Columns);
        Assert.Equal(500, Assert.Single(metrics.Limits));
        Assert.Equal(limit, read.Rows);
        Assert.DoesNotContain(capture.Entities, entity => entity is AppImage or ImageLike);
    }

    [Fact]
    public async Task Create_AnnUnderfill_AppendsOneExactTopUpWithoutDuplicates()
    {
        var user = await SeedAsync(2, 650);
        var capture = new QueryCapture();
        await using var context = database.CreateContext(new LimitAnnRows(3), capture);
        var id = await Repository(context).CreateAsync(user, 20, new([], []));
        Assert.Equal(2, capture.Reads.Count(sql => sql.Contains(ForMeFeedRepository.AnnQueryTag)));
        Assert.Single(capture.Reads, sql => sql.Contains(ForMeFeedRepository.ExactTopUpQueryTag));
        var ids = await context.ForMeFeedEntries.Where(entry => entry.FeedId == id).OrderBy(entry => entry.Rank)
            .Select(entry => entry.ImageId).ToListAsync();
        Assert.Equal(500, ids.Count);
        Assert.Equal(500, ids.Distinct().Count());
        var firstVector = await context.ImageLikes.Where(like => like.UserId == user)
            .OrderByDescending(like => like.CreatedAtUtc).ThenByDescending(like => like.ImageId)
            .Select(like => like.Image!.Embedding!).FirstAsync();
        var expected = await context.Images.Where(image => !image.Likes.Any(like => like.UserId == user))
            .OrderBy(image => image.Embedding!.L2Distance(firstVector) + 0.0).ThenBy(image => image.Id)
            .Select(image => image.Id).ToListAsync();
        var firstAnnIds = ids.Take(6).ToArray();
        Assert.Equal(expected.Where(image => !firstAnnIds.Contains(image)).Take(494), ids.Skip(6));
    }

    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(2, 20, 1)]
    public async Task Create_SmallOrUnpersonalizedPool_PreservesFallbackAndBoundedTopUp(int references, int candidates, int topUps)
    {
        var user = await SeedAsync(references, candidates);
        var capture = new QueryCapture();
        await using var context = database.CreateContext(capture);
        var id = await Repository(context).CreateAsync(user, 20, new([], []));
        Assert.Equal(topUps, capture.Reads.Count(sql => sql.Contains(ForMeFeedRepository.ExactTopUpQueryTag)));
        var feed = await context.ForMeFeeds.SingleAsync(feed => feed.Id == id);
        Assert.Equal(references == 0 ? "Fallback" : "Personalized", feed.Mode);
        Assert.Equal(candidates, feed.Total);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0.7.4")]
    [InlineData("not-a-version")]
    [InlineData("0.8.0-beta")]
    public async Task Create_UnsupportedExtension_FailsClearlyWithoutPublishing(string? version)
    {
        var user = await SeedAsync(1, 4);
        await using var context = database.CreateContext(new ExtensionVersion(version));
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Repository(context).CreateAsync(user, 20, new([], [])));
        Assert.Contains("pgvector", failure.Message);
        Assert.Contains("0.8.0", failure.Message);
        Assert.Empty(await context.ForMeFeeds.ToListAsync());
    }

    [Theory]
    [InlineData("commit")]
    [InlineData("rollback")]
    [InlineData("cancel")]
    public async Task Create_TransactionEnds_RestoresSameConnectionSearchSettings(string outcome)
    {
        var references = outcome == "cancel" ? 100 : 1;
        var user = await SeedAsync(references, 20);
        using var cancellation = new CancellationTokenSource();
        await using var context = database.CreateContext(new EndGeneration(outcome, cancellation));
        await context.Database.OpenConnectionAsync();
        // Exercise the extension before recording settings so its custom defaults are registered.
        await context.Database.ExecuteSqlRawAsync("SELECT '[0]'::vector <-> '[1]'::vector");
        await context.Database.ExecuteSqlRawAsync("SET hnsw.ef_search = 73; SET hnsw.max_scan_tuples = 17000; SET hnsw.scan_mem_multiplier = 3; SET hnsw.iterative_scan = 'strict_order'; SET random_page_cost = 2.3");
        var before = await SettingsAsync(context);
        if (outcome == "commit")
            await Repository(context).CreateAsync(user, 20, new([], []));
        else if (outcome == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Repository(context, references).CreateAsync(user, 20, new([], []), cancellation.Token));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => Repository(context).CreateAsync(user, 20, new([], [])));
        Assert.Equal(before, await SettingsAsync(context));
        if (outcome != "commit") Assert.Empty(await context.ForMeFeeds.ToListAsync());
        // Restore our test's session values before returning this connection to the fixture pool.
        await context.Database.ExecuteSqlRawAsync("RESET hnsw.ef_search; RESET hnsw.max_scan_tuples; RESET hnsw.scan_mem_multiplier; RESET hnsw.iterative_scan; RESET random_page_cost");
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task Create_PlannerCostOverride_OnlyAppliesDuringUnfilteredAnn(bool filtered, int references)
    {
        var user = await SeedAsync(references, 20);
        var capture = new PlannerCostCapture();
        var logger = new SearchLogger();
        await using var context = database.CreateContext(capture);
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("SET random_page_cost = 2.3");
        try
        {
            var filters = new ForMeFilters(filtered ? [(int)AiUsageClassification.HumanMade] : [], []);
            var repository = Repository(context, 25, logger);
            var id = await repository.CreateAsync(user, 20, filters);
            var created = Assert.Single(logger.Events);
            if (references == 0)
            {
                Assert.Empty(capture.Values);
                Assert.False(created.ContainsKey("AppliedAnnRandomPageCost"));
            }
            else
            {
                Assert.Equal(filtered ? "2.3" : "1.1", Assert.Single(capture.Values, value => value.Ann).Cost);
                Assert.Equal("2.3", Assert.Single(capture.Values, value => !value.Ann).Cost);
                Assert.Equal(!filtered, created.ContainsKey("AppliedAnnRandomPageCost"));
                if (!filtered) Assert.Equal(1.1, created["AppliedAnnRandomPageCost"]);
            }
            Assert.Equal("2.3", await context.Database.SqlQuery<string>($"""
                SELECT current_setting('random_page_cost') AS "Value"
                """).SingleAsync());
            await repository.ReadAsync(id, new(user, false), 0);
            Assert.False(logger.Events[^1].ContainsKey("AppliedAnnRandomPageCost"));
            Assert.False(logger.Events[^1].ContainsKey("AnnCandidateLimit"));
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("RESET random_page_cost");
        }
    }

    private static async Task<string> SettingsAsync(RepositoryContext context)
        => await context.Database.SqlQuery<string>($"""
            SELECT current_setting('hnsw.ef_search') || ',' || current_setting('hnsw.max_scan_tuples') || ',' ||
                current_setting('hnsw.scan_mem_multiplier') || ',' || current_setting('hnsw.iterative_scan') || ',' || current_setting('random_page_cost') AS "Value"
            """).SingleAsync();

    private ForMeFeedRepository Repository(RepositoryContext context, int references = 25, ILogger<ForMeFeedRepository>? logger = null)
        => new(context, new TestClock(), logger ?? NullLogger<ForMeFeedRepository>.Instance,
            Options.Create(new ForMeFeedOptions { RepresentativeCount = references }));

    private async Task<int> SeedAsync(int references, int candidates)
    {
        await using var context = database.CreateContext();
        await context.ForMeFeeds.ExecuteDeleteAsync();
        await context.Images.ExecuteDeleteAsync();
        await context.Tags.ExecuteDeleteAsync();
        await context.AppUsers.ExecuteDeleteAsync();
        var user = new AppUser { Login = "vector-test", NormalizedLogin = "VECTOR-TEST", PasswordHash = "unused" };
        context.AppUsers.Add(user);
        await context.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;
        var liked = Enumerable.Range(0, references).Select(index => Image(index * 100f)).ToArray();
        context.Images.AddRange(liked.Concat(Enumerable.Range(1, candidates).Select(index => Image(index))));
        await context.SaveChangesAsync();
        context.ImageLikes.AddRange(liked.Select(image => new ImageLike { ImageId = image.Id, UserId = user.Id, CreatedAtUtc = now }));
        await context.SaveChangesAsync();
        return user.Id;

        UserMadeImage Image(float value) => new()
        {
            UploadedById = user.Id, Source = ImageSource.UserUpload, UploadedAtUtc = now,
            Visibility = ImageVisibility.Gallery, ModerationStatus = ModerationStatus.Approved,
            AiUsage = AiUsageClassification.HumanMade, StorageKey = Guid.NewGuid().ToString("N"),
            ContentType = "image/jpeg", Width = 1, Height = 1, Embedding = Vector(value), EmbeddingStatus = EmbeddingStatus.Ready
        };
    }

    private static Vector Vector(float value)
    {
        var dimensions = new float[1024];
        dimensions[0] = value;
        return new Vector(dimensions);
    }

    private sealed class SearchLogger : ILogger<ForMeFeedRepository>
    {
        public List<Dictionary<string, object?>> Events { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Events.Add(((IEnumerable<KeyValuePair<string, object?>>)(object)state!).ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    private sealed class PlannerCostCapture : DbCommandInterceptor
    {
        public List<(bool Ann, string Cost)> Values { get; } = [];
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            var ann = command.CommandText.Contains(ForMeFeedRepository.AnnQueryTag);
            if (ann || command.CommandText.Contains(ForMeFeedRepository.ExactTopUpQueryTag))
            {
                await using var probe = command.Connection!.CreateCommand();
                probe.Transaction = command.Transaction;
                probe.CommandText = "SELECT current_setting('random_page_cost')";
                Values.Add((ann, (string)(await probe.ExecuteScalarAsync(cancellationToken))!));
                if (ann)
                {
                    probe.CommandText = "SELECT current_setting('hnsw.ef_search') || ',' || current_setting('hnsw.max_scan_tuples') || ',' || current_setting('hnsw.scan_mem_multiplier') || ',' || current_setting('hnsw.iterative_scan')";
                    Assert.Equal($"{ForMeFeedRepository.SearchEffort},{ForMeFeedRepository.MaxScanTuples},{ForMeFeedRepository.ScanMemoryMultiplier},relaxed_order",
                        (string)(await probe.ExecuteScalarAsync(cancellationToken))!);
                }
            }
            return result;
        }
    }

    private sealed class AnnReadMetrics : DbCommandInterceptor
    {
        private readonly ImageRepositoryReadMetrics _metrics = new();
        public IReadOnlyList<ImageRepositoryReadMetrics.Read> Reads => _metrics.Reads;
        public List<int> Limits { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(ForMeFeedRepository.AnnQueryTag)) Limits.Add((int)AnnLimitParameter.Find(command).Value!);
            return ValueTask.FromResult(result);
        }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
            => command.CommandText.Contains(ForMeFeedRepository.AnnQueryTag)
                ? _metrics.ReaderExecutedAsync(command, eventData, result, cancellationToken)
                : ValueTask.FromResult(result);
    }

    [Fact]
    public async Task LimitAnnRows_UserIdEqualsLimit_ChangesOnlySqlLimitParameter()
    {
        using var command = new Npgsql.NpgsqlCommand("-- ForMeAnnNeighbors\nSELECT 1 WHERE @userId = 500\nLIMIT @limit");
        command.Parameters.AddWithValue("userId", 500);
        command.Parameters.AddWithValue("limit", 500);
        await new LimitAnnRows(3).ReaderExecutingAsync(command, null!, default);
        Assert.Equal(500, command.Parameters["userId"].Value);
        Assert.Equal(3, command.Parameters["limit"].Value);
        command.CommandText = "-- ForMeAnnNeighbors\nSELECT @userId";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new LimitAnnRows(3).ReaderExecutingAsync(command, null!, default));
        Assert.Contains("SQL LIMIT", error.Message);
    }

    private sealed class LimitAnnRows(int limit) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(ForMeFeedRepository.AnnQueryTag))
                AnnLimitParameter.Find(command).Value = limit;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ExtensionVersion(string? version) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.Contains("pg_extension")) return ValueTask.FromResult(result);
            var table = new DataTable();
            table.Columns.Add("Value", typeof(string));
            if (version is not null) table.Rows.Add(version);
            return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()));
        }
    }

    private sealed class EndGeneration(string outcome, CancellationTokenSource cancellation) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(ForMeFeedRepository.AnnQueryTag))
            {
                if (outcome == "rollback") throw new InvalidOperationException("Test rollback");
                if (outcome == "cancel")
                {
                    cancellation.Cancel();
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            return ValueTask.FromResult(result);
        }
    }
}
