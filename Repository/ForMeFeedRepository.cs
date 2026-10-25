using System.Data;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Configuration;

namespace Repository;

public sealed class ForMeFeedRepository(RepositoryContext context, TimeProvider clock, ILogger<ForMeFeedRepository> logger, IOptions<ForMeFeedOptions> options) : IForMeFeedRepository
{
    // Two transaction-scoped slots in a feature-specific two-int advisory-lock namespace.
    public const int GenerationLockNamespace = 0x464F524D;
    internal const string AnnQueryTag = "ForMeAnnNeighbors";
    internal const string TagResolutionQueryTag = "ForMeExcludedTagResolution";
    internal const string ExactTopUpQueryTag = "ForMeExactTopUp";
    internal const int AnnCandidateLimit = 2000;
    internal const int FilteredAnnCandidateLimit = 1000;
    internal const int SearchEffort = 200;
    internal const int ScanMemoryMultiplier = 2;
    internal const int MaxScanTuples = 20000;
    internal const double AnnRandomPageCost = 1.1;

    public async Task<Guid> CreateAsync(int userId, int pageSize, ForMeFilters filters,
        CancellationToken cancellationToken = default)
    {
        var telemetry = new ForMeRepositoryTelemetry(logger, "ForMeFeedCreated");
        try
        {
            IReadOnlyList<int> orderedIds;
            var mode = "Fallback";
            using var generationSlot = telemetry.Measure("GenerationSlot");
            await using (var generation = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,
                             cancellationToken))
            {
                var acquired = false;
                for (var slot = 0; slot < 2 && !acquired; slot++)
                    acquired = await context.Database.SqlQuery<bool>($"""
                        SELECT pg_try_advisory_xact_lock({GenerationLockNamespace}, {slot}) AS "Value"
                        """).SingleAsync(cancellationToken);
                if (!acquired)
                    throw new ForMeFeedLimitException("Recommendation generation is busy. Retry later.", 2);

                generationSlot.Dispose();
                telemetry.ConfiguredRepresentativeCount = options.Value.RepresentativeCount;
                var annCandidateLimit = GetAnnCandidateLimit(filters);
                telemetry.AnnCandidateLimit = annCandidateLimit;
                telemetry.AnnCandidateRowCount = 0;
                telemetry.ExactTopUpQueryCount = 0;
                telemetry.ExactTopUpImageCount = 0;
                using (telemetry.Measure("VectorSearchSetup"))
                {
                    var installed = await context.Database.SqlQuery<string>($"""
                        SELECT extversion AS "Value" FROM pg_extension WHERE extname = 'vector'
                        """).SingleOrDefaultAsync(cancellationToken);
                    if (!Version.TryParse(installed, out var version) || version < new Version(0, 8, 0))
                        throw new InvalidOperationException("Personalized feeds require a released pgvector version 0.8.0 or newer. Upgrade the vector extension before creating a feed.");
                    await ConfigureVectorSearchAsync(context, SearchEffort, ScanMemoryMultiplier, MaxScanTuples, cancellationToken);
                }
                ForMeFilters generationFilters;
                using (telemetry.Measure("ExcludedTagResolution"))
                {
                    // The normalized request contains at most 20 names and NormalizedName is unique.
                    // Resolve metadata within this generation snapshot; retain original names for future pages.
                    var approvedNames = filters.ExcludedTags.Length == 0 ? [] : await context.Tags.AsNoTracking()
                        .TagWith(TagResolutionQueryTag)
                        .Where(tag => tag.ModerationStatus == TagModerationStatus.Approved &&
                            filters.ExcludedTags.Contains(tag.NormalizedName))
                        .Select(tag => tag.NormalizedName).ToArrayAsync(cancellationToken);
                    telemetry.ResolvedExcludedTagCount = approvedNames.Length;
                    generationFilters = new ForMeFilters(filters.AiUsage, approvedNames);
                }
                List<Pgvector.Vector> seeds;
                using (telemetry.Measure("SeedLoad"))
                {
                    seeds = await context.ImageLikes.AsNoTracking()
                        .Where(like => like.UserId == userId && like.Image!.Embedding != null &&
                                       like.Image.DeletedAtUtc == null && like.Image.Visibility == ImageVisibility.Gallery &&
                                       like.Image.ModerationStatus == ModerationStatus.Approved)
                        .OrderByDescending(like => like.CreatedAtUtc).ThenByDescending(like => like.ImageId)
                        .Select(like => like.Image!.Embedding!).Take(100).ToListAsync(cancellationToken);
                }
                telemetry.SeedCount = seeds.Count;
                IReadOnlyList<int> representatives;
                using (telemetry.Measure("RepresentativeSelection"))
                    representatives = ForMeRanking.SelectRepresentatives(seeds.Select(seed => seed.ToArray()).ToArray(), options.Value.RepresentativeCount);
                telemetry.RepresentativeCount = representatives.Count;
                var candidates = Eligible(userId, generationFilters).Where(image => image.Embedding != null);
                var neighborhoods = new List<IReadOnlyList<int>>();
                using (telemetry.Measure("NeighborQueries"))
                {
                    telemetry.NeighborQueryCount = 0;
                    string? previousRandomPageCost = null;
                    if (representatives.Count > 0 && filters.AiUsage.Length == 0 && filters.ExcludedTags.Length == 0)
                    {
                        previousRandomPageCost = await ApplyAnnPlannerCostAsync(context, cancellationToken);
                        telemetry.AppliedAnnRandomPageCost = AnnRandomPageCost;
                    }
                    foreach (var representative in representatives)
                    {
                        var vector = seeds[representative];
                        // Keep the inner distance order bare so pgvector can use HNSW.
                        // Only a bounded shortlist of IDs and exact distances leaves PostgreSQL.
                        var neighbors = await candidates.TagWith(AnnQueryTag)
                            .OrderBy(image => image.Embedding!.L2Distance(vector))
                            .Select(image => new { image.Id, Distance = image.Embedding!.L2Distance(vector) })
                            .Take(annCandidateLimit).ToListAsync(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        neighborhoods.Add(neighbors.OrderBy(row => row.Distance).ThenBy(row => row.Id)
                            .Take(500).Select(row => row.Id).ToArray());
                        telemetry.AnnCandidateRowCount += neighbors.Count;
                        telemetry.NeighborQueryCount++;
                    }
                    if (previousRandomPageCost is not null)
                        await RestoreAnnPlannerCostAsync(context, previousRandomPageCost, cancellationToken);
                }
                using (telemetry.Measure("Merge"))
                    orderedIds = ForMeRanking.Merge(neighborhoods);
                cancellationToken.ThrowIfCancellationRequested();
                if (representatives.Count > 0 && orderedIds.Count < 500)
                {
                    using var topUpTiming = telemetry.Measure("ExactTopUp");
                    telemetry.ExactTopUpQueryCount = 1;
                    var mergedIds = orderedIds.ToArray();
                    var firstVector = seeds[representatives[0]];
                    var topUp = await candidates.TagWith(ExactTopUpQueryTag)
                        .Where(image => !mergedIds.Contains(image.Id))
                        .OrderBy(image => image.Embedding!.L2Distance(firstVector) + 0.0)
                        .ThenBy(image => image.Id).Select(image => image.Id)
                        .Take(500 - orderedIds.Count).ToListAsync(cancellationToken);
                    telemetry.ExactTopUpImageCount = topUp.Count;
                    orderedIds = orderedIds.Concat(topUp).ToArray();
                }
                telemetry.Mode = orderedIds.Count > 0 ? "Personalized" : "Fallback";
                if (orderedIds.Count > 0)
                    mode = "Personalized";
                else
                {
                    using var fallbackQuery = telemetry.Measure("FallbackQuery");
                    orderedIds = await Eligible(userId, generationFilters).OrderByDescending(image => image.Likes.Count)
                        .ThenByDescending(image => image.UploadedAtUtc).ThenByDescending(image => image.Id)
                        .Select(image => image.Id).Take(500).ToListAsync(cancellationToken);
                }
                using (telemetry.Measure("GenerationCommit"))
                    await generation.CommitAsync(cancellationToken);
            }

            // Do not hold a user-row lock during vector scans. READ COMMITTED gives the count below
            // a fresh snapshot after another publisher releases this row, unlike REPEATABLE READ.
            using var publicationTiming = telemetry.Measure("Publication");
            await using var publication = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,
                cancellationToken);
            var users = await context.Database.SqlQuery<int>($"""
                SELECT "Id" AS "Value" FROM "AppUsers" WHERE "Id" = {userId} FOR UPDATE
                """).ToListAsync(cancellationToken);
            if (users.Count == 0) throw new Base401UnauthorizedException("Account session is no longer valid.");
            var now = clock.GetUtcNow();
            if (await context.ForMeFeeds.CountAsync(feed => feed.UserId == userId && feed.ExpiresAtUtc > now,
                    cancellationToken) >= 100)
                throw new ForMeFeedLimitException("The active recommendation feed limit was reached. Retry later.", 60);

            // Retain key-share locks until insert commits; an existence check alone has a hard-delete race.
            var ids = orderedIds.ToArray();
            var surviving = ids.Length == 0 ? [] : await context.Database.SqlQuery<int>($"""
                SELECT "Id" AS "Value" FROM "Images" WHERE "Id" = ANY({ids}) ORDER BY "Id" FOR KEY SHARE
                """).ToListAsync(cancellationToken);
            var survivingIds = surviving.ToHashSet();
            var storedIds = orderedIds.Where(survivingIds.Contains).ToArray();
            now = clock.GetUtcNow();
            var feedId = Guid.NewGuid();
            var feed = new ForMeFeed
            {
                Id = feedId, UserId = userId, Mode = mode, PageSize = Math.Clamp(pageSize, 1, 50),
                AiUsage = filters.AiUsage, ExcludedTags = filters.ExcludedTags,
                CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(1), Total = storedIds.Length,
                Entries = storedIds.Select((id, index) => new ForMeFeedEntry
                    { FeedId = feedId, Rank = index + 1, ImageId = id }).ToList()
            };
            context.ForMeFeeds.Add(feed);
            await context.SaveChangesAsync(cancellationToken);
            await publication.CommitAsync(cancellationToken);
            telemetry.StoredImageCount = storedIds.Length;
            return feedId;
        }
        catch (Exception exception) { telemetry.Failed(exception); throw; }
        finally { telemetry.Emit(); }
    }

    public async Task<ForMeFeedPage> ReadAsync(Guid feedId, ImageViewer viewer, int afterRank,
        CancellationToken cancellationToken = default)
    {
        var telemetry = new ForMeRepositoryTelemetry(logger, "ForMeFeedPageRead");
        try
        {
            using var snapshotLoad = telemetry.Measure("SnapshotLoad");
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,
                cancellationToken);
            var feed = await context.ForMeFeeds.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == feedId && candidate.UserId == viewer.UserId,
                    cancellationToken)
                ?? throw new Base409ConflictException("Recommendation feed is unavailable. Restart without a cursor.");
            if (feed.ExpiresAtUtc <= clock.GetUtcNow())
                throw new Base409ConflictException("Recommendation feed expired. Restart without a cursor.");
            snapshotLoad.Dispose();
            using var pageSelection = telemetry.Measure("PageSelection");
            var eligible = Eligible(feed.UserId, new ForMeFilters(feed.AiUsage, feed.ExcludedTags));
            var rows = await context.ForMeFeedEntries.AsNoTracking()
                .Where(entry => entry.FeedId == feedId && entry.Rank > afterRank)
                .Join(eligible, entry => entry.ImageId, image => image.Id,
                    (entry, image) => new { entry.Rank, image.Id })
                .OrderBy(row => row.Rank).Take(feed.PageSize + 1).ToListAsync(cancellationToken);
            var page = rows.Take(feed.PageSize).ToArray();
            pageSelection.Dispose();
            using var cardHydration = telemetry.Measure("CardHydration");
            var imageIds = page.Select(row => row.Id).ToArray();
            var cards = await ImageCardQuery.Project(context.Images.AsNoTracking()
                    .Where(image => Enumerable.Contains(imageIds, image.Id)), viewer)
                .ToDictionaryAsync(image => image.Id, cancellationToken);
            cardHydration.Dispose();
            using (telemetry.Measure("ReadCommit"))
                await transaction.CommitAsync(cancellationToken);
            var result = new ForMeFeedPage(feed, page.Select(row => cards[row.Id]).ToArray(),
                page.Length == 0 ? afterRank : page[^1].Rank, rows.Count > feed.PageSize);
            telemetry.Mode = feed.Mode;
            telemetry.ReturnedImageCount = result.Images.Count;
            telemetry.HasMore = result.HasMore;
            return result;
        }
        catch (Exception exception) { telemetry.Failed(exception); throw; }
        finally { telemetry.Emit(); }
    }

    public Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        return context.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "ForMeFeeds" WHERE "Id" IN (
                SELECT "Id" FROM "ForMeFeeds" WHERE "ExpiresAtUtc" <= {now}
                ORDER BY "ExpiresAtUtc", "Id" LIMIT 1000 FOR UPDATE SKIP LOCKED
            )
            """, cancellationToken);
    }

    internal static async Task<string> ApplyAnnPlannerCostAsync(RepositoryContext database,
        CancellationToken cancellationToken = default)
    {
        var previous = await database.Database.SqlQuery<string>($"""
            SELECT current_setting('random_page_cost') AS "Value"
            """).SingleAsync(cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('random_page_cost', {AnnRandomPageCost.ToString(CultureInfo.InvariantCulture)}, true)
            """, cancellationToken);
        return previous;
    }

    internal static Task RestoreAnnPlannerCostAsync(RepositoryContext database, string previous,
        CancellationToken cancellationToken = default)
        => database.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('random_page_cost', {previous}, true)
            """, cancellationToken);

    internal static int GetAnnCandidateLimit(ForMeFilters filters)
        => filters.AiUsage.Length == 0 && filters.ExcludedTags.Length == 0
            ? AnnCandidateLimit : FilteredAnnCandidateLimit;

    internal static Task ConfigureVectorSearchAsync(RepositoryContext database, int searchEffort,
        int scanMemoryMultiplier, int maxScanTuples, CancellationToken cancellationToken = default)
        => database.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('hnsw.iterative_scan', 'relaxed_order', true),
                   set_config('hnsw.max_scan_tuples', {maxScanTuples.ToString(CultureInfo.InvariantCulture)}, true),
                   set_config('hnsw.scan_mem_multiplier', {scanMemoryMultiplier.ToString(CultureInfo.InvariantCulture)}, true),
                   set_config('hnsw.ef_search', {searchEffort.ToString(CultureInfo.InvariantCulture)}, true)
            """, cancellationToken);

    private IQueryable<AppImage> Eligible(int userId, ForMeFilters filters)
    {
        var query = context.Images.AsNoTracking().Where(image => image.DeletedAtUtc == null &&
            image.Visibility == ImageVisibility.Gallery && image.ModerationStatus == ModerationStatus.Approved &&
            !image.Likes.Any(like => like.UserId == userId));
        if (filters.AiUsage.Length > 0)
        {
            var classifications = filters.AiUsage.Select(value => (AiUsageClassification)value).ToArray();
            query = query.Where(image => Enumerable.Contains(classifications, image.AiUsage));
        }
        if (filters.ExcludedTags.Length > 0)
            query = query.Where(image => !image.Tags.Any(tag => tag.ModerationStatus == TagModerationStatus.Approved &&
                Enumerable.Contains(filters.ExcludedTags, tag.NormalizedName)));
        return query;
    }
}
