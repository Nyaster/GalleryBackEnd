using Contracts;
using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Repository;

namespace GallerySiteIntegrationTests;

public sealed partial class ForMeFeedPerformanceTests
{
    private async Task CalibrateIndexConstructionAsync(TimeProvider clock, AppUser[] users)
    {
        Report("INDEX CONSTRUCTION DIAGNOSTIC: fixed data/query settings; not production acceptance.");
        var profile = new SearchProfile(ForMeFeedRepository.AnnCandidateLimit, 200, 2, 20000);
        var workloads = new List<Workload>();
        foreach (var filters in new[] { new ForMeFilters([], []), new ForMeFilters([1], ["exclude-performance"]) })
        {
            var capture = new PlanCapture();
            await using (var context = database.CreateContext(capture))
                await FeedRepository(context, clock).CreateAsync(users[0].Id, 20, filters);
            workloads.Add(new Workload(filters.AiUsage.Length == 0 ? "unfiltered" : "filtered17percent", filters,
                capture.Queries, await ExactNeighborhoodsAsync(capture.Queries), 0));
        }
        await RecordIndexAsync("baseline m16/ef_construction64");
        await CheckIndexQualityAsync(workloads, profile);
        foreach (var construction in new[] { (M: 16, Effort: 128), (M: 16, Effort: 256), (M: 32, Effort: 256) })
        {
            var allGraphsPass = true;
            for (var graph = 1; graph <= 3; graph++)
            {
                await RebuildIndexAsync(construction.M, construction.Effort);
                await RecordIndexAsync($"m{construction.M}/ef_construction{construction.Effort}; independent_graph={graph}");
                if (await CheckIndexQualityAsync(workloads, profile)) continue;
                allGraphsPass = false;
                break;
            }
            if (!allGraphsPass) continue;
            // Exact baselines do not depend on graph topology; measure once on the same fixed data.
            for (var index = 0; index < workloads.Count; index++)
            {
                var workload = workloads[index];
                if (workload.ExactTenP95 != 0) continue;
                var oldTen = await MeasureAsync(() => ExactSearchPhaseAsync(users[0].Id, workload.Filters, 10),
                    () => ExactSearchPhaseAsync(users[1].Id, workload.Filters, 10));
                var exactTwentyFive = await MeasureAsync(() => ExactSearchPhaseAsync(users[0].Id, workload.Filters, 25),
                    () => ExactSearchPhaseAsync(users[1].Id, workload.Filters, 25));
                Report($"{workload.Name}: exact10_search_p95_ms={oldTen:F2}; exact25_search_p95_ms={exactTwentyFive:F2}");
                workloads[index] = workload with { ExactTenP95 = oldTen };
            }
            if (!await MeasureProfilePerformanceAsync(workloads, users, clock, profile, calibrating: true)) continue;
            Report($"SELECTED INDEX m={construction.M}; ef_construction={construction.Effort}; {profile}; THREE independent graphs passed accuracy; full generation/paging gates passed. Production migration/constants and normal acceptance remain required.");
            return;
        }
        Assert.Fail("No bounded index construction profile passed all three-graph quality and full performance gates. See " + ReportPath);
    }

    private async Task<bool> CheckIndexQualityAsync(IReadOnlyList<Workload> workloads, SearchProfile profile)
    {
        if (!await ExplainAsync(workloads[0], profile, 0))
        {
            Report("Index construction candidate rejected: unfiltered query did not use HNSW.");
            return false;
        }
        foreach (var workload in workloads)
        {
            var quality = await RecallAsync(workload, profile);
            var indexUsed = await ExplainAsync(workload, profile, quality.WorstIndex);
            var passed = quality.Passes && (workload.Name != "unfiltered" || indexUsed);
            Report($"INDEX QUALITY {workload.Name}: {profile}; mean_recall={quality.Mean:P3}; minimum_recall={quality.Minimum:P3}; final_feed_overlap={quality.FeedOverlap:P3}; gates_pass={passed}");
            if (!passed) return false;
        }
        return true;
    }

    private async Task RebuildIndexAsync(int m, int constructionEffort)
    {
        var createSql = (m, constructionEffort) switch
        {
            (16, 128) => "CREATE INDEX \"IX_Images_Embedding\" ON \"Images\" USING hnsw (\"Embedding\" vector_l2_ops) WITH (m = 16, ef_construction = 128) WHERE \"Embedding\" IS NOT NULL",
            (16, 256) => "CREATE INDEX \"IX_Images_Embedding\" ON \"Images\" USING hnsw (\"Embedding\" vector_l2_ops) WITH (m = 16, ef_construction = 256) WHERE \"Embedding\" IS NOT NULL",
            (32, 256) => "CREATE INDEX \"IX_Images_Embedding\" ON \"Images\" USING hnsw (\"Embedding\" vector_l2_ops) WITH (m = 32, ef_construction = 256) WHERE \"Embedding\" IS NOT NULL",
            _ => throw new ArgumentOutOfRangeException(nameof(constructionEffort))
        };
        await using var context = database.CreateContext();
        context.Database.SetCommandTimeout(TimeSpan.FromMinutes(20));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await context.Database.ExecuteSqlRawAsync("SET LOCAL maintenance_work_mem = '1GB'; SET LOCAL max_parallel_maintenance_workers = 0");
        await context.Database.ExecuteSqlRawAsync("DROP INDEX \"IX_Images_Embedding\"");
        Report($"Building isolated index m={m}; ef_construction={constructionEffort}; query settings/gates unchanged.");
        await context.Database.ExecuteSqlRawAsync(createSql);
        await transaction.CommitAsync();
        Report($"Built isolated index m={m}; ef_construction={constructionEffort}; build_ms={watch.Elapsed.TotalMilliseconds:F2}");
    }

    private async Task RecordIndexAsync(string label)
    {
        await using var context = database.CreateContext();
        var definition = await context.Database.SqlQuery<string>($"""
            SELECT pg_get_indexdef(c.oid) || '; reloptions=' || coalesce(array_to_string(c.reloptions, ','), 'defaults') ||
                '; valid=' || i.indisvalid::text || '; size_bytes=' || pg_relation_size(c.oid)::text AS "Value"
            FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid WHERE c.relname = 'IX_Images_Embedding'
            """).SingleAsync();
        Report($"INDEX {label}: {definition}");
    }
}
