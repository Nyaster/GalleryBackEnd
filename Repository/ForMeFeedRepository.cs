using System.Data;
using Microsoft.Extensions.Logging;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace Repository;

public sealed class ForMeFeedRepository(RepositoryContext context, TimeProvider clock, ILogger<ForMeFeedRepository> logger) : IForMeFeedRepository
{
    // Two transaction-scoped slots in a feature-specific two-int advisory-lock namespace.
    public const int GenerationLockNamespace = 0x464F524D;

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
                    representatives = ForMeRanking.SelectRepresentatives(seeds.Select(seed => seed.ToArray()).ToArray());
                telemetry.RepresentativeCount = representatives.Count;
                var candidates = Eligible(userId, filters).Where(image => image.Embedding != null);
                var neighborhoods = new List<IReadOnlyList<int>>();
                using (telemetry.Measure("NeighborQueries"))
                {
                    telemetry.NeighborQueryCount = 0;
                    foreach (var representative in representatives)
                    {
                        var vector = seeds[representative];
                        // An expression rather than the bare distance operator deliberately requests exact retrieval.
                        // Keep the vectors in PostgreSQL: only the bounded neighbor IDs cross this boundary.
                        neighborhoods.Add(await candidates.OrderBy(image => image.Embedding!.L2Distance(vector) + 0.0)
                            .ThenBy(image => image.Id).Select(image => image.Id).Take(500).ToListAsync(cancellationToken));
                        telemetry.NeighborQueryCount++;
                    }
                }
                using (telemetry.Measure("Merge"))
                    orderedIds = ForMeRanking.Merge(neighborhoods);
                telemetry.Mode = orderedIds.Count > 0 ? "Personalized" : "Fallback";
                if (orderedIds.Count > 0)
                    mode = "Personalized";
                else
                {
                    using var fallbackQuery = telemetry.Measure("FallbackQuery");
                    orderedIds = await Eligible(userId, filters).OrderByDescending(image => image.Likes.Count)
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
