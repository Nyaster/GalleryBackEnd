using Contracts;
using Microsoft.EntityFrameworkCore;
using Repository;
using Shared.DataTransferObjects;

namespace Infrastructure.Persistence;

public sealed class ImageViewRepository(RepositoryContext context) : IImageViewRepository
{
    public async Task ApplyAsync(ImageViewBatch batch, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        const string pruneReceipts =
            """
            WITH expired AS (
                SELECT "Id" FROM "ImageViewBatchReceipts"
                WHERE "CreatedAtUtc" <= statement_timestamp() - INTERVAL '24 hours'
                ORDER BY "CreatedAtUtc"
                LIMIT 1000
                FOR UPDATE SKIP LOCKED
            )
            DELETE FROM "ImageViewBatchReceipts" r USING expired
            WHERE r."Id" = expired."Id"
            """;
        await context.Database.ExecuteSqlRawAsync(pruneReceipts, cancellationToken);

        FormattableString claimReceipt =
            $"""
            INSERT INTO "ImageViewBatchReceipts" ("Id", "CreatedAtUtc")
            SELECT {batch.Id}, {batch.CreatedAtUtc}
            WHERE {batch.CreatedAtUtc} > statement_timestamp() - INTERVAL '24 hours'
            ON CONFLICT ("Id") DO NOTHING
            """;
        var claimed = await context.Database.ExecuteSqlAsync(claimReceipt, cancellationToken);
        if (claimed == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var totals = batch.Totals.ToArray();
        var imageIds = totals.Select(entry => entry.Key).ToArray();
        var viewCounts = totals.Select(entry => entry.Value).ToArray();
        // Recheck age after the receipt claim, which may have waited for an expired receipt to be removed.
        // Key-share locks defer hard deletion. The ordered counter upsert serializes writes for each image.
        FormattableString updateTotals =
            $"""
            WITH valid_images AS (
                SELECT i."Id", totals.view_count
                FROM unnest({imageIds}::integer[], {viewCounts}::bigint[]) AS totals(image_id, view_count)
                JOIN "Images" i ON i."Id" = totals.image_id
                WHERE {batch.CreatedAtUtc} > statement_timestamp() - INTERVAL '24 hours'
                ORDER BY i."Id"
                FOR KEY SHARE OF i
            ), applied AS (
                INSERT INTO "ImageViewCounters" ("ImageId", "TotalViews", "UniqueRegisteredViewers")
                SELECT "Id", view_count, 0 FROM valid_images ORDER BY "Id"
                ON CONFLICT ("ImageId") DO UPDATE
                SET "TotalViews" = "ImageViewCounters"."TotalViews" + excluded."TotalViews"
                RETURNING "ImageId"
            )
            SELECT "ImageId" AS "Value" FROM applied
            """;
        var appliedImageIds = await context.Database.SqlQuery<int>(updateTotals).ToArrayAsync(cancellationToken);

        if (appliedImageIds.Length > 0 && batch.FirstSeen.Count > 0)
        {
            var firstSeen = batch.FirstSeen.ToArray();
            var firstSeenImageIds = firstSeen.Select(entry => entry.Key.ImageId).ToArray();
            var userIds = firstSeen.Select(entry => entry.Key.UserId).ToArray();
            var timestamps = firstSeen.Select(entry => entry.Value).ToArray();

            // With the counter locks held, this statement sees all previously committed first observations.
            FormattableString updateUniqueViewers =
                $"""
                WITH observations AS MATERIALIZED (
                    SELECT observation.*
                    FROM unnest({firstSeenImageIds}::integer[], {userIds}::integer[], {timestamps}::timestamptz[])
                        AS observation(image_id, user_id, seen_at_utc)
                    JOIN "Images" i ON i."Id" = observation.image_id
                    WHERE i."Id" = ANY ({appliedImageIds})
                        AND i."UploadedById" IS DISTINCT FROM observation.user_id
                ), earlier_views AS (
                    UPDATE "ImageViewFirstSeen" first_seen
                    SET "SeenAtUtc" = observation.seen_at_utc
                    FROM observations observation
                    WHERE first_seen."ImageId" = observation.image_id
                        AND first_seen."UserId" = observation.user_id
                        AND first_seen."SeenAtUtc" > observation.seen_at_utc
                ), new_viewers AS (
                    INSERT INTO "ImageViewFirstSeen" ("ImageId", "UserId", "SeenAtUtc")
                    SELECT observation.image_id, observation.user_id, observation.seen_at_utc
                    FROM observations observation
                    JOIN "AppUsers" u ON u."Id" = observation.user_id
                    WHERE NOT EXISTS (
                        SELECT 1 FROM "ImageViewFirstSeen" first_seen
                        WHERE first_seen."ImageId" = observation.image_id AND first_seen."UserId" = observation.user_id
                    )
                    RETURNING "ImageId"
                ), unique_counts AS (
                    SELECT "ImageId", count(*) AS new_count FROM new_viewers GROUP BY "ImageId"
                )
                UPDATE "ImageViewCounters" counter
                SET "UniqueRegisteredViewers" = counter."UniqueRegisteredViewers" + unique_counts.new_count
                FROM unique_counts
                WHERE counter."ImageId" = unique_counts."ImageId"
                """;
            await context.Database.ExecuteSqlAsync(updateUniqueViewers, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ImageViewTotals> GetTotalsAsync(int imageId, CancellationToken cancellationToken)
    {
        FormattableString readTotals =
            $"""
            SELECT "TotalViews", "UniqueRegisteredViewers"
            FROM "ImageViewCounters"
            WHERE "ImageId" = {imageId}
            """;
        return await context.Database.SqlQuery<ImageViewTotals>(readTotals).SingleOrDefaultAsync(cancellationToken)
               ?? new ImageViewTotals(0, 0);
    }

    public async Task<IReadOnlyList<ImageViewDay>> GetDailyAsync(int imageId, DateOnly from, DateOnly through,
        CancellationToken cancellationToken)
    {
        FormattableString readDaily =
            $"""
            SELECT ("SeenAtUtc" AT TIME ZONE 'UTC')::date AS "Date", count(*) AS "NewUniqueViewers"
            FROM "ImageViewFirstSeen"
            WHERE "ImageId" = {imageId}
                AND "SeenAtUtc" >= ({from}::date::timestamp AT TIME ZONE 'UTC')
                AND "SeenAtUtc" < (({through}::date + 1)::timestamp AT TIME ZONE 'UTC')
            GROUP BY "Date"
            ORDER BY "Date"
            """;
        return await context.Database.SqlQuery<ImageViewDay>(readDaily).ToListAsync(cancellationToken);
    }
}
