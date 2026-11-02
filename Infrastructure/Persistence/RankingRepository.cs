using System.Data;
using Contracts;
using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Repository;

public sealed class RankingRepository(RepositoryContext context) : IRankingRepository
{
    // Rank discoverable images before applying the viewer's AI classification filter.
    private const string RankedImagesCte = """
        WITH scores AS (
            SELECT "ImageId", SUM(CASE WHEN "Type" = 'Like' THEN 1 ELSE -1 END)::integer AS "LikeDelta"
            FROM "ImageLikeActivities"
            WHERE "OccurredAtUtc" >= @startUtc AND "OccurredAtUtc" < @endUtc
            GROUP BY "ImageId"
            HAVING SUM(CASE WHEN "Type" = 'Like' THEN 1 ELSE -1 END) > 0
        ), ranked AS (
            SELECT image."Id" AS "ImageId", scores."LikeDelta", image."AiUsage",
                ROW_NUMBER() OVER (ORDER BY scores."LikeDelta" DESC, image."UploadedAtUtc" DESC, image."Id" DESC)::integer AS "Rank"
            FROM scores JOIN "Images" AS image ON image."Id" = scores."ImageId"
            WHERE image."DeletedAtUtc" IS NULL AND image."Visibility" = 'Gallery'
                AND image."ModerationStatus" = 'Approved'
        )
        """;

    public Task<DateTimeOffset?> GetEarliestLikeActivityUtcAsync(CancellationToken cancellationToken = default)
        => context.ImageLikeActivities.AsNoTracking().OrderBy(activity => activity.OccurredAtUtc)
            .Select(activity => (DateTimeOffset?)activity.OccurredAtUtc).FirstOrDefaultAsync(cancellationToken);

    public Task<List<RankingSnapshotKey>> GetSnapshotKeysAsync(CancellationToken cancellationToken = default)
        => context.RankingSnapshots.AsNoTracking()
            .Select(snapshot => new RankingSnapshotKey(snapshot.Period, snapshot.PeriodStartUtc))
            .ToListAsync(cancellationToken);

    public Task<RankingSnapshotMetadata?> GetSnapshotAsync(RankingPeriod period, DateTimeOffset startUtc,
        CancellationToken cancellationToken = default)
        => context.RankingSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.Period == period && snapshot.PeriodStartUtc == startUtc)
            .Select(snapshot => new RankingSnapshotMetadata(snapshot.Id, snapshot.PeriodStartUtc, snapshot.PeriodEndUtc))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<RankingPage> GetLivePageAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, int page, int pageSize,
        ImageViewer viewer, IReadOnlyList<AiUsageClassification>? aiUsage, CancellationToken cancellationToken = default)
        => ReadPageAsync(context.Database.SqlQueryRaw<RankingRow>(
            RankedImagesCte + "\n" + """SELECT "ImageId", "LikeDelta", "AiUsage", "Rank" FROM ranked""",
            new NpgsqlParameter("startUtc", startUtc), new NpgsqlParameter("endUtc", endUtc)),
            page, pageSize, viewer, aiUsage, cancellationToken);

    public Task<RankingPage> GetArchivePageAsync(int snapshotId, int page, int pageSize, ImageViewer viewer,
        IReadOnlyList<AiUsageClassification>? aiUsage, CancellationToken cancellationToken = default)
        => ReadPageAsync(context.RankingSnapshotEntries.AsNoTracking().Where(entry => entry.SnapshotId == snapshotId)
            .Join(context.Images.AsNoTracking().Where(image => image.DeletedAtUtc == null &&
                image.Visibility == ImageVisibility.Gallery && image.ModerationStatus == ModerationStatus.Approved),
                entry => entry.ImageId, image => image.Id, (entry, image) => new RankingRow
                {
                    ImageId = image.Id, LikeDelta = entry.LikeDelta, Rank = entry.Rank, AiUsage = image.AiUsage.ToString()
                }), page, pageSize, viewer, aiUsage, cancellationToken);

    private async Task<RankingPage> ReadPageAsync(IQueryable<RankingRow> query, int page, int pageSize,
        ImageViewer viewer, IReadOnlyList<AiUsageClassification>? aiUsage, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        if (aiUsage is { Count: > 0 })
        {
            var classifications = aiUsage.Distinct().Select(value => value.ToString()).ToArray();
            query = query.Where(row => classifications.Contains(row.AiUsage));
        }
        var total = await query.CountAsync(cancellationToken);
        var size = Math.Clamp(pageSize, 1, 50);
        var offset = ((long)Math.Max(page, 1) - 1) * size;
        IReadOnlyList<RankedImageCard> entries = [];
        if (offset < total)
        {
            var rows = await query.OrderBy(row => row.Rank).Skip((int)offset).Take(size).ToListAsync(cancellationToken);
            var ids = rows.Select(row => row.ImageId).ToArray();
            var cards = await ImageCardQuery.Project(context.Images.AsNoTracking().Where(image => ids.Contains(image.Id)), viewer)
                .ToDictionaryAsync(image => image.Id, cancellationToken);
            entries = rows.Select(row => new RankedImageCard(row.Rank, row.LikeDelta, cards[row.ImageId])).ToArray();
        }
        await transaction.CommitAsync(cancellationToken);
        return new RankingPage(total, entries);
    }

    public async Task<bool> TryCreateSnapshotAsync(RankingPeriod period, DateTimeOffset startUtc,
        DateTimeOffset endUtc, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // Materialize INSERT ... RETURNING directly; LINQ composition would produce invalid SQL.
        var ids = await context.Database.SqlQuery<int>($"""
            INSERT INTO "RankingSnapshots" ("Period", "PeriodStartUtc", "PeriodEndUtc", "CreatedAtUtc")
            VALUES ({period.ToString()}, {startUtc}, {endUtc}, {now})
            ON CONFLICT ("Period", "PeriodStartUtc") DO NOTHING
            RETURNING "Id" AS "Value"
            """).ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }
        await context.Database.ExecuteSqlRawAsync(RankedImagesCte + """

            INSERT INTO "RankingSnapshotEntries" ("SnapshotId", "ImageId", "Rank", "LikeDelta")
            SELECT @snapshotId, "ImageId", "Rank", "LikeDelta" FROM ranked
            """, new object[] { new NpgsqlParameter("startUtc", startUtc), new NpgsqlParameter("endUtc", endUtc),
                new NpgsqlParameter("snapshotId", ids[0]) }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public sealed class RankingRow
    {
        public int ImageId { get; set; }
        public int LikeDelta { get; set; }
        public int Rank { get; set; }
        public string AiUsage { get; set; } = string.Empty;
    }
}
