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
using Pgvector.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Repository;
using Xunit.Abstractions;

namespace GallerySiteIntegrationTests;

[Collection("Image repository performance")]
public sealed partial class ForMeFeedPerformanceTests(PostgresFixture database, ITestOutputHelper output)
    : IClassFixture<PostgresFixture>
{
    private static readonly string ReportPath = Path.Combine(Path.GetTempPath(), "gallery-for-me-indexed-benchmark.log");

    [Fact]
    public async Task QualityBaseline_CapturedAnnQuery_ReplaysExactClosestFiveHundred()
    {
        await using var setup = database.CreateContext();
        await setup.ForMeFeeds.ExecuteDeleteAsync();
        await setup.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Images\" CASCADE");
        await setup.AppUsers.ExecuteDeleteAsync();
        var user = new AppUser { Login = "quality-harness", NormalizedLogin = "QUALITY-HARNESS", PasswordHash = "unused" };
        setup.AppUsers.Add(user);
        await setup.SaveChangesAsync();
        var images = Enumerable.Range(0, 601).Select(index =>
        {
            var vector = new float[1024];
            vector[0] = index;
            return new UserMadeImage
            {
                Source = ImageSource.UserUpload, Visibility = ImageVisibility.Gallery,
                ModerationStatus = ModerationStatus.Approved, UploadedAtUtc = DateTimeOffset.UtcNow,
                StorageKey = "quality-harness/" + index, ContentType = "image/jpeg", Width = 1, Height = 1,
                Embedding = new Vector(vector), EmbeddingStatus = EmbeddingStatus.Ready
            };
        }).ToArray();
        setup.Images.AddRange(images);
        await setup.SaveChangesAsync();
        setup.ImageLikes.Add(new ImageLike { UserId = user.Id, ImageId = images[0].Id, CreatedAtUtc = DateTimeOffset.UtcNow });
        await setup.SaveChangesAsync();
        var capture = new PlanCapture();
        await using var query = database.CreateContext(capture);
        await FeedRepository(query, new TestClock()).CreateAsync(user.Id, 20, new([], []));
        Assert.Single(capture.Queries);
        AssertCapturedLimit(capture.Queries, ForMeFeedRepository.AnnCandidateLimit);
        var exact = await ExactNeighborhoodsAsync(capture.Queries);
        Assert.Equal(images.Skip(1).Take(500).Select(image => image.Id), Assert.Single(exact).Select(row => row.Id));
        var recall = await RecallAsync(new Workload("harness", new([], []), capture.Queries, exact, 0), ProductionProfile);
        Assert.Equal(1d, recall.Mean);
        Assert.Equal(1d, recall.Minimum);
        Assert.Equal(1d, recall.FeedOverlap);
    }

    private static SearchProfile ProductionProfile => new(ForMeFeedRepository.AnnCandidateLimit,
        ForMeFeedRepository.SearchEffort, ForMeFeedRepository.ScanMemoryMultiplier, ForMeFeedRepository.MaxScanTuples);

    private static IReadOnlyList<SearchProfile> CalibrationProfiles =>
    [
        new(ForMeFeedRepository.AnnCandidateLimit, 200, 2, 20000), new(ForMeFeedRepository.AnnCandidateLimit, 200, 4, 20000), new(ForMeFeedRepository.AnnCandidateLimit, 200, 8, 20000),
        new(ForMeFeedRepository.AnnCandidateLimit, 400, 8, 20000), new(ForMeFeedRepository.AnnCandidateLimit, 1000, 8, 20000), new(ForMeFeedRepository.AnnCandidateLimit, 1000, 8, 40000)
    ];

    [ImageRepositoryPerformanceFact]
    public Task Feed_HundredThousandDenseVectors_MeetsGenerationPagingAndRecallBudgets()
        => RunAcceptanceAsync(100_000);

    [ImageRepositoryPerformanceFact]
    public Task Feed_SeventeenThousandDenseVectors_MeetsGenerationPagingAndRecallBudgets()
        => RunAcceptanceAsync(17_000);

    private async Task RunAcceptanceAsync(int imageCount)
    {
        var calibrating = Environment.GetEnvironmentVariable("GALLERY_FOR_ME_CALIBRATE") == "1";
        var indexCalibrating = Environment.GetEnvironmentVariable("GALLERY_FOR_ME_INDEX_CALIBRATE") == "1";
        File.WriteAllText(ReportPath, "");
        Report($"mode={(indexCalibrating ? "INDEX CONSTRUCTION DIAGNOSTIC; production verification still required" : calibrating ? "CALIBRATION; production verification still required" : "PRODUCTION ACCEPTANCE")}; " +
               $"OS={RuntimeInformation.OSDescription}; CPU={Environment.ProcessorCount}; framework={RuntimeInformation.FrameworkDescription}; " +
#if DEBUG
               "configuration=Debug");
#else
               "configuration=Release");
#endif
        Report("Comparison: old exact10 SEARCH PHASE excludes publication; new25 FULL GENERATION includes setup and publication. This is a conservative speedup gate.");
        var clock = new TestClock();
        var users = await SeedAsync(clock, imageCount);
        if (indexCalibrating)
        {
            await CalibrateIndexConstructionAsync(clock, users);
            return;
        }
        await using (var metadata = database.CreateContext())
            Report("database=" + await metadata.Database.SqlQuery<string>($"""
                SELECT version() || '; pgvector=' || extversion AS "Value" FROM pg_extension WHERE extname = 'vector'
                """).SingleAsync());
        await using (var metadata = database.CreateContext())
        {
            var indexDefinition = await metadata.Database.SqlQuery<string>($"""
                SELECT pg_get_indexdef(c.oid) AS "Value" FROM pg_class c WHERE c.relname='IX_Images_Embedding'
                """).SingleAsync();
            Assert.Contains("m='16'", indexDefinition);
            Assert.Contains("ef_construction='128'", indexDefinition);
            Report("production_index=" + indexDefinition);
        }
        IReadOnlyList<SearchProfile> profiles = calibrating ? CalibrationProfiles : [ProductionProfile];
        var workloads = new List<Workload>();
        var cases = new (string Name, ForMeFilters Filters, bool Baseline)[]
        {
            ("unfiltered", new([], []), true),
            ("filtered17percent", new([1], ["exclude-performance"]), true),
            ("broadAiUsage", new(Enum.GetValues<AiUsageClassification>().Select(value => (int)value).ToArray(), []), false),
            ("broadExcludedTag", new([], ["nonexistent-performance-tag"]), false)
        };
        foreach (var (name, filters, baseline) in cases)
        {
            var capture = new PlanCapture();
            await using (var context = database.CreateContext(capture))
                await FeedRepository(context, clock).CreateAsync(users[0].Id, 20, filters);
            Assert.Equal(25, capture.Queries.Count);
            var expectedLimit = ForMeFeedRepository.AnnCandidateLimit;
            AssertCapturedLimit(capture.Queries, expectedLimit);
            Report($"{name}: effective_candidate_limit={expectedLimit}; captured_production_queries={capture.Queries.Count}");
            if (name == "unfiltered")
            {
                var viablePlan = false;
                foreach (var profile in profiles)
                {
                    if (!await ExplainAsync(new Workload(name, filters, capture.Queries, [], 0), profile, 0)) continue;
                    viablePlan = true;
                    break;
                }
                Assert.True(viablePlan, "No allowed profile uses HNSW; baseline timings were not run.");
            }
            IReadOnlyList<IReadOnlyList<Neighbor>> exact;
            if (baseline)
                exact = await ExactNeighborhoodsAsync(capture.Queries);
            else
            {
                await using var equivalence = database.CreateContext();
                if (name == "broadAiUsage")
                {
                    var classifications = Enum.GetValues<AiUsageClassification>();
                    Assert.False(await equivalence.Images.AnyAsync(image => !classifications.Contains(image.AiUsage)));
                }
                else
                    Assert.False(await equivalence.Tags.AnyAsync(tag => tag.NormalizedName == "nonexistent-performance-tag"));
                var unfiltered = workloads[0];
                for (var reference = 0; reference < capture.Queries.Count; reference++)
                    Assert.Equal(((Vector)unfiltered.Queries[reference].Parameters.Single(parameter => parameter.Value is Vector).Value!).ToArray(),
                        ((Vector)capture.Queries[reference].Parameters.Single(parameter => parameter.Value is Vector).Value!).ToArray());
                exact = unfiltered.Exact;
                Report($"{name}: eligible set and ordered reference equivalence verified; cached unfiltered exact neighborhoods reused.");
            }
            var oldTen = 0d;
            if (baseline)
            {
                oldTen = await MeasureAsync(() => ExactSearchPhaseAsync(users[0].Id, filters, 10),
                    () => ExactSearchPhaseAsync(users[1].Id, filters, 10));
                var exactTwentyFive = await MeasureAsync(() => ExactSearchPhaseAsync(users[0].Id, filters, 25),
                    () => ExactSearchPhaseAsync(users[1].Id, filters, 25));
                Report($"{name}: exact10_search_p95_ms={oldTen:F2}; exact25_search_p95_ms={exactTwentyFive:F2}");
            }
            workloads.Add(new Workload(name, filters, capture.Queries, exact, oldTen));
        }

        SearchProfile? selected = null;
        foreach (var profile in profiles)
        {
            if (!await ExplainAsync(workloads[0], profile, 0))
            {
                Report($"unfiltered: {profile}; index_gate_pass=False; recall not accepted as ANN evidence");
                continue;
            }
            var qualities = new List<Quality>();
            var indexPlansPass = true;
            foreach (var workload in workloads)
            {
                var quality = await RecallAsync(workload, profile);
                qualities.Add(quality);
                var indexUsed = await ExplainAsync(workload, profile, quality.WorstIndex);
                Report($"{workload.Name}: retrieval_plan={(indexUsed ? "HNSW ANN" : "exact planner choice; recall is not ANN fidelity evidence")}");
                if (workload.Name == "unfiltered" && !indexUsed)
                {
                    indexPlansPass = false;
                    Report($"{workload.Name}: {profile}; index_gate_pass=False");
                }
                Report($"{workload.Name}: {profile}; mean_recall_at_500={quality.Mean:P3}; " +
                       $"min_recall_at_500={quality.Minimum:P3}; final_feed_overlap={quality.FeedOverlap:P3}");
                if (!quality.Passes || !indexPlansPass) break;
            }
            if (!indexPlansPass || qualities.Count != workloads.Count || qualities.Any(quality => !quality.Passes)) continue;
            if (!await MeasureProfilePerformanceAsync(workloads, users, clock, profile, calibrating)) continue;
            selected = profile;
            break;
        }
        Report($"selected_profile={selected?.ToString() ?? "NONE"}; production_profile={ProductionProfile}");
        Assert.NotNull(selected);
        if (calibrating)
            Report("Calibration completed. Set the selected production constants, then run normal acceptance without GALLERY_FOR_ME_CALIBRATE.");
        else
            Report("Configured production profile passed all recall, generation, index-use and paging gates.");
    }

    private async Task<bool> MeasureProfilePerformanceAsync(IReadOnlyList<Workload> workloads, AppUser[] users,
        TimeProvider clock, SearchProfile profile, bool calibrating)
    {
        var generationsPass = true;
        foreach (var workload in workloads)
        {
            await using (var clean = database.CreateContext()) await clean.ForMeFeeds.ExecuteDeleteAsync();
            var generation = await MeasureAsync(() => GenerateAsync(users[0].Id), () => GenerateAsync(users[1].Id));
            var passed = generation <= 5000 && (workload.Name != "unfiltered" || generation < workload.ExactTenP95);
            generationsPass &= passed;
            Report($"{workload.Name}: {profile}; new25_full_generation_p95_ms={generation:F2}; generation_gates_pass={passed}");

            async Task<double> GenerateAsync(int userId)
            {
                await using var context = calibrating ? database.CreateContext(new ProfileOverride(profile)) : database.CreateContext();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var watch = Stopwatch.StartNew();
                try
                {
                    var id = await FeedRepository(context, clock).CreateAsync(userId, 20, workload.Filters, deadline.Token);
                    var elapsed = watch.Elapsed.TotalMilliseconds;
                    Assert.Equal(500, await context.ForMeFeedEntries.CountAsync(entry => entry.FeedId == id));
                    return elapsed;
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested)
                {
                    Report($"{workload.Name}: {profile}; generation exceeded shared10s deadline");
                    return double.PositiveInfinity;
                }
            }
        }
        if (!generationsPass) return false;
        var pageP95 = await MeasurePagingAsync(users[0].Id, clock, profile, calibrating);
        Report($"paging: {profile}; concurrency=10; samples=100; p95_ms={pageP95:F2}");
        if (pageP95 > 300) return false;
        return true;
    }

    private readonly Lock _reportLock = new();
    private void Report(string value)
    {
        lock (_reportLock)
        {
            output.WriteLine(value);
            File.AppendAllText(ReportPath, DateTimeOffset.UtcNow.ToString("O") + " " + value + Environment.NewLine);
        }
    }

    private static ForMeFeedRepository FeedRepository(RepositoryContext context, TimeProvider clock)
        => new(context, clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<ForMeFeedRepository>.Instance,
            Microsoft.Extensions.Options.Options.Create(new Shared.Configuration.ForMeFeedOptions()));

    private async Task<double> ExactSearchPhaseAsync(int userId, ForMeFilters filters, int count)
    {
        await using var context = database.CreateContext();
        var watch = Stopwatch.StartNew();
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var seeds = await context.ImageLikes.AsNoTracking().Where(like => like.UserId == userId &&
            like.Image!.Embedding != null && like.Image.DeletedAtUtc == null &&
            like.Image.Visibility == ImageVisibility.Gallery && like.Image.ModerationStatus == ModerationStatus.Approved)
            .OrderByDescending(like => like.CreatedAtUtc).ThenByDescending(like => like.ImageId)
            .Select(like => like.Image!.Embedding!).Take(100).ToListAsync();
        var representatives = ForMeRanking.SelectRepresentatives(seeds.Select(seed => seed.ToArray()).ToArray(), count);
        var candidates = context.Images.AsNoTracking().Where(image => image.DeletedAtUtc == null &&
            image.Visibility == ImageVisibility.Gallery && image.ModerationStatus == ModerationStatus.Approved &&
            image.Embedding != null && !image.Likes.Any(like => like.UserId == userId));
        if (filters.AiUsage.Length > 0)
        {
            var classifications = filters.AiUsage.Select(value => (AiUsageClassification)value).ToArray();
            candidates = candidates.Where(image => classifications.Contains(image.AiUsage));
        }
        if (filters.ExcludedTags.Length > 0)
            candidates = candidates.Where(image => !image.Tags.Any(tag => tag.ModerationStatus == TagModerationStatus.Approved &&
                filters.ExcludedTags.Contains(tag.NormalizedName)));
        var neighborhoods = new List<IReadOnlyList<int>>();
        foreach (var representative in representatives)
        {
            var vector = seeds[representative];
            neighborhoods.Add(await candidates.OrderBy(image => image.Embedding!.L2Distance(vector) + 0.0)
                .ThenBy(image => image.Id).Select(image => image.Id).Take(500).ToListAsync());
        }
        Assert.Equal(500, ForMeRanking.Merge(neighborhoods).Count);
        await transaction.CommitAsync();
        return watch.Elapsed.TotalMilliseconds;
    }

    private static async Task<double> MeasureAsync(Func<Task<double>> first, Func<Task<double>> second)
    {
        for (var warmup = 0; warmup < 3; warmup++) await Task.WhenAll(first(), second());
        var times = new List<double>();
        for (var sample = 0; sample < 10; sample++) times.AddRange(await Task.WhenAll(first(), second()));
        return Percentile95(times);
    }

    private async Task<double> MeasurePagingAsync(int userId, TimeProvider clock, SearchProfile profile, bool calibrating)
    {
        await using var setup = calibrating ? database.CreateContext(new ProfileOverride(profile)) : database.CreateContext();
        var feedId = await FeedRepository(setup, clock).CreateAsync(userId, 20, new([], []));
        async Task<double> PageAsync()
        {
            await using var context = database.CreateContext();
            var watch = Stopwatch.StartNew();
            var page = await FeedRepository(context, clock).ReadAsync(feedId, new(userId, false), 20);
            Assert.Equal(20, page.Images.Count);
            return watch.Elapsed.TotalMilliseconds;
        }
        for (var warmup = 0; warmup < 3; warmup++)
            await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => PageAsync()));
        var times = new List<double>();
        for (var sample = 0; sample < 10; sample++)
            times.AddRange(await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => PageAsync())));
        return Percentile95(times);
    }

    private async Task<List<IReadOnlyList<Neighbor>>> ExactNeighborhoodsAsync(IReadOnlyList<NeighborQuery> queries)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var neighborhoods = new List<IReadOnlyList<Neighbor>>();
        foreach (var query in queries) neighborhoods.Add(await ReadNeighborsAsync(context, query, true, 500));
        await transaction.CommitAsync();
        return neighborhoods;
    }

    private static void AssertCapturedLimit(IReadOnlyList<NeighborQuery> queries, int expectedLimit)
        => Assert.All(queries, query =>
        {
            using var command = new NpgsqlCommand(query.Sql);
            foreach (var parameter in query.Parameters) command.Parameters.Add(parameter.Clone());
            Assert.Equal(expectedLimit, AnnLimitParameter.Find(command).Value);
        });

    private async Task<Quality> RecallAsync(Workload workload, SearchProfile profile)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await ForMeFeedRepository.ConfigureVectorSearchAsync(context, profile.Effort, profile.MemoryMultiplier, profile.MaxScanTuples);
        var previousCost = workload.Filters.AiUsage.Length == 0 && workload.Filters.ExcludedTags.Length == 0
            ? await ForMeFeedRepository.ApplyAnnPlannerCostAsync(context) : null;
        var settings = await context.Database.SqlQuery<string>($"""
            SELECT 'work_mem=' || current_setting('work_mem') || ', ef_search=' || current_setting('hnsw.ef_search') ||
                ', memory=' || current_setting('hnsw.scan_mem_multiplier') || ', max_scan=' || current_setting('hnsw.max_scan_tuples') ||
                ', iterative=' || current_setting('hnsw.iterative_scan') || ', random_page_cost=' || current_setting('random_page_cost') AS "Value"
            """).SingleAsync();
        Report($"{workload.Name}: {profile}; actual_settings={settings}");
        var neighborhoods = new List<IReadOnlyList<int>>();
        var recalls = new List<double>();
        for (var index = 0; index < workload.Queries.Count; index++)
        {
            var rows = await ReadNeighborsAsync(context, workload.Queries[index], false, profile.CandidateLimit);
            Assert.InRange(rows.Count, 0, profile.CandidateLimit);
            var nearest = rows.OrderBy(row => row.Distance).ThenBy(row => row.Id).Take(500).ToArray();
            var ids = nearest.Select(row => row.Id).ToArray();
            neighborhoods.Add(ids);
            var missing = workload.Exact[index].Where(row => !ids.Contains(row.Id)).ToArray();
            var recall = 1 - missing.Length / (double)workload.Exact[index].Count;
            recalls.Add(recall);
            var cutoff = nearest.Length == 0 ? double.NaN : nearest[^1].Distance;
            Report($"{workload.Name}: {profile}; representative={index}; ann_rows={rows.Count}; retained={nearest.Length}; " +
                   $"exact_hits={workload.Exact[index].Count - missing.Length}; recall_at_500={recall:P3}; " +
                   $"exact500_distance={workload.Exact[index][^1].Distance:F6}; ann_cutoff={cutoff:F6}; " +
                   $"missing_distance_range={(missing.Length == 0 ? "none" : $"{missing.Min(row => row.Distance):F6}..{missing.Max(row => row.Distance):F6}")}");
        }
        if (previousCost is not null) await ForMeFeedRepository.RestoreAnnPlannerCostAsync(context, previousCost);
        var exactFeed = ForMeRanking.Merge(workload.Exact.Select(neighbors => (IReadOnlyList<int>)neighbors.Select(row => row.Id).ToArray()).ToArray());
        var actualFeed = ForMeRanking.Merge(neighborhoods);
        await transaction.CommitAsync();
        var minimum = recalls.Min();
        return new Quality(recalls.Average(), minimum, actualFeed.Intersect(exactFeed).Count() / (double)exactFeed.Count,
            recalls.IndexOf(minimum));
    }

    private async Task<bool> ExplainAsync(Workload workload, SearchProfile profile, int representative)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await ForMeFeedRepository.ConfigureVectorSearchAsync(context, profile.Effort, profile.MemoryMultiplier, profile.MaxScanTuples);
        var previousCost = workload.Filters.AiUsage.Length == 0 && workload.Filters.ExcludedTags.Length == 0
            ? await ForMeFeedRepository.ApplyAnnPlannerCostAsync(context) : null;
        await using var command = Command(context, workload.Queries[representative], false, profile.CandidateLimit);
        command.CommandText = "EXPLAIN (ANALYZE, BUFFERS) " + command.CommandText;
        var plan = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) plan.Add(reader.GetString(0));
        var text = System.Text.RegularExpressions.Regex.Replace(string.Join(Environment.NewLine, plan),
            @"'\[[^\]]*\]'::vector", "'[embedding omitted]'::vector");
        Report($"{workload.Name}: {profile}; worst_representative={representative}; EXPLAIN with production-local settings:" + Environment.NewLine + text);
        if (previousCost is not null) await ForMeFeedRepository.RestoreAnnPlannerCostAsync(context, previousCost);
        await transaction.CommitAsync();
        return text.Contains("IX_Images_Embedding", StringComparison.Ordinal);
    }

    private static async Task<List<Neighbor>> ReadNeighborsAsync(RepositoryContext context, NeighborQuery query, bool exact, int limit)
    {
        await using var command = Command(context, query, exact, limit);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<Neighbor>();
        while (await reader.ReadAsync()) rows.Add(new Neighbor(reader.GetInt32(0), reader.GetDouble(1)));
        return rows;
    }

    private static DbCommand Command(RepositoryContext context, NeighborQuery query, bool exact, int limit)
    {
        var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = query.Sql;
        command.CommandTimeout = 120;
        foreach (var parameter in query.Parameters) command.Parameters.Add(parameter.Clone());
        var limitStart = command.CommandText.LastIndexOf("\nLIMIT ", StringComparison.Ordinal);
        Assert.True(limitStart >= 0);
        AnnLimitParameter.Find(command).Value = limit;
        if (!exact) return command;
        var orderStart = command.CommandText.LastIndexOf("\nORDER BY ", StringComparison.Ordinal);
        Assert.True(orderStart >= 0 && limitStart > orderStart, "Expected one bare-distance ANN ordering.");
        var distance = command.CommandText[(orderStart + "\nORDER BY ".Length)..limitStart];
        var idColumn = System.Text.RegularExpressions.Regex.Match(command.CommandText, "SELECT ([a-zA-Z0-9_]+\\.\\\"Id\\\")").Groups[1].Value;
        Assert.NotEmpty(idColumn);
        command.CommandText = command.CommandText[..orderStart] + "\nORDER BY (" + distance + ") + 0.0, " + idColumn + command.CommandText[limitStart..];
        return command;
    }

    private sealed record Neighbor(int Id, double Distance);
    private sealed record NeighborQuery(string Sql, List<NpgsqlParameter> Parameters);
    private sealed record Workload(string Name, ForMeFilters Filters, IReadOnlyList<NeighborQuery> Queries,
        IReadOnlyList<IReadOnlyList<Neighbor>> Exact, double ExactTenP95);
    private sealed record Quality(double Mean, double Minimum, double FeedOverlap, int WorstIndex)
    {
        public bool Passes => Mean >= 0.95 && Minimum >= 0.90;
    }
    private sealed record SearchProfile(int CandidateLimit, int Effort, int MemoryMultiplier, int MaxScanTuples)
    {
        public override string ToString() => $"candidates={CandidateLimit}; ef_search={Effort}; memory={MemoryMultiplier}; max_scan={MaxScanTuples}";
    }

    private async Task<AppUser[]> SeedAsync(TimeProvider clock, int imageCount)
    {
        await using var context = database.CreateContext();
        await context.ForMeFeeds.ExecuteDeleteAsync();
        await context.Images.ExecuteDeleteAsync();
        await context.AppUsers.ExecuteDeleteAsync();
        await context.Tags.ExecuteDeleteAsync();
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
        var centers = Enumerable.Range(0, 25).Select(_ => Enumerable.Range(0, 1024)
            .Select(_ => random.NextSingle() * 2 - 1).ToArray()).ToArray();
        await using (var copy = await connection.BeginBinaryImportAsync("""
            COPY "Images" ("Id", "Source", "UploadedById", "UploadedAtUtc", "Visibility", "ModerationStatus",
                "AiUsage", "StorageKey", "ContentType", "Width", "Height", "Embedding", "EmbeddingStatus",
                "EmbeddingAttempts", "image_type") FROM STDIN (FORMAT BINARY)
            """))
        {
            copy.Timeout = TimeSpan.FromMinutes(10);
            for (var index = 1; index <= imageCount; index++)
            {
                var vector = new float[1024];
                for (var dimension = 0; dimension < vector.Length; dimension++)
                    vector[dimension] = centers[index % 25][dimension] * 3 + random.NextSingle() * 0.4f - 0.2f;
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
        var indexBuildWatch = Stopwatch.StartNew();
        Report("COPY finished; building production HNSW index with fixture-only maintenance_work_mem=1GB and serial construction; production m=16/ef_construction=128 (calibration baseline uses64).");
        await using (var indexBuild = await context.Database.BeginTransactionAsync())
        {
            await context.Database.ExecuteSqlRawAsync("SET LOCAL maintenance_work_mem = '1GB'; SET LOCAL max_parallel_maintenance_workers = 0");
            var indexSql = Environment.GetEnvironmentVariable("GALLERY_FOR_ME_INDEX_CALIBRATE") == "1"
                ? "CREATE INDEX \"IX_Images_Embedding\" ON \"Images\" USING hnsw (\"Embedding\" vector_l2_ops) WITH (m=16, ef_construction=64) WHERE \"Embedding\" IS NOT NULL"
                : "CREATE INDEX \"IX_Images_Embedding\" ON \"Images\" USING hnsw (\"Embedding\" vector_l2_ops) WITH (m=16, ef_construction=128) WHERE \"Embedding\" IS NOT NULL";
            await context.Database.ExecuteSqlRawAsync(indexSql);
            await indexBuild.CommitAsync();
        }
        Report($"Fixture baseline index build_ms={indexBuildWatch.Elapsed.TotalMilliseconds:F2}");
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AppImageImageTag" ("AppImagesId", "TagsId")
            SELECT "Id", {tag.Id} FROM "Images" WHERE "Id" % 3 = 0
            """);
        foreach (var user in users.Take(2))
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ImageLikes" ("ImageId", "UserId", "CreatedAtUtc")
                SELECT ((series * 9973) % {imageCount}) + 1, {user.Id}, {clock.GetUtcNow()}
                FROM generate_series(1, 100) AS series
                """);
        foreach (var user in users.Skip(2))
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ImageLikes" ("ImageId", "UserId", "CreatedAtUtc")
                SELECT ((series * 7919 + {user.Id} * 17) % {imageCount}) + 1, {user.Id}, {clock.GetUtcNow()}
                FROM generate_series(1, 1000) AS series
                """);
        await context.Database.ExecuteSqlRawAsync("ANALYZE");
        var vectors = await context.ImageLikes.Where(like => like.UserId == users[0].Id)
            .OrderByDescending(like => like.CreatedAtUtc).ThenByDescending(like => like.ImageId)
            .Select(like => like.Image!.Embedding!).ToListAsync();
        Assert.Equal(25, ForMeRanking.SelectRepresentatives(vectors.Select(vector => vector.ToArray()).ToArray(), 25).Count);
        Report($"fixture={imageCount} images, dense1024D, 25 visual clusters, currentLikes=50200, approvedTagLinks={imageCount / 3}; " +
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
        public List<NeighborQuery> Queries { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(ForMeFeedRepository.AnnQueryTag))
                Queries.Add(new NeighborQuery(command.CommandText,
                    command.Parameters.Cast<NpgsqlParameter>().Select(parameter => parameter.Clone()).ToList()));
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task ProfileOverride_UserIdEqualsLimit_ChangesOnlySqlLimitParameter()
    {
        using var command = new NpgsqlCommand("-- ForMeAnnNeighbors\nSELECT 1 WHERE @userId = 500\nLIMIT @limit");
        command.Parameters.AddWithValue("userId", 500);
        command.Parameters.AddWithValue("limit", 500);
        var interceptor = new ProfileOverride(ProductionProfile with { CandidateLimit = 3 });
        await interceptor.ReaderExecutingAsync(command, null!, default);
        Assert.Equal(500, command.Parameters["userId"].Value);
        Assert.Equal(3, command.Parameters["limit"].Value);
        command.CommandText = "-- ForMeAnnNeighbors\nSELECT @userId";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await interceptor.ReaderExecutingAsync(command, null!, default));
        Assert.Contains("SQL LIMIT", error.Message);
    }

    private sealed class ProfileOverride(SearchProfile profile) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("set_config('hnsw.ef_search'"))
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(command.CommandText,
                    "set_config\\('(?<setting>hnsw\\.[^']+)',\\s*@(?<parameter>[a-zA-Z0-9_]+)");
                Assert.Equal(3, matches.Count);
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var value = match.Groups["setting"].Value switch
                    {
                        "hnsw.ef_search" => profile.Effort,
                        "hnsw.max_scan_tuples" => profile.MaxScanTuples,
                        "hnsw.scan_mem_multiplier" => profile.MemoryMultiplier,
                        _ => throw new InvalidOperationException("Unexpected calibration setting")
                    };
                    command.Parameters[match.Groups["parameter"].Value].Value = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(ForMeFeedRepository.AnnQueryTag))
                AnnLimitParameter.Find(command).Value = profile.CandidateLimit;
            return ValueTask.FromResult(result);
        }
    }
}
