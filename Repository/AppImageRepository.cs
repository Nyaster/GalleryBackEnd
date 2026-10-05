using Contracts;
using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Shared.DataTransferObjects;

namespace Repository;

public sealed class AppImageRepository(RepositoryContext context) : IAppImageRepository
{
    public Task<AppImage?> GetByIdAsync(int id, bool trackChanges, CancellationToken cancellationToken = default)
        => ImageQuery(trackChanges).SingleOrDefaultAsync(image => image.Id == id, cancellationToken);

    public Task<AppImage?> GetWithTagsByIdAsync(int id, CancellationToken cancellationToken = default)
        => context.Images.Include(image => image.Tags).SingleOrDefaultAsync(image => image.Id == id, cancellationToken);

    public Task<ImageMetadata?> GetMetadataByIdAsync(int id, CancellationToken cancellationToken = default)
        => context.Images.AsNoTracking().Where(image => image.Id == id)
            .Select(image => new ImageMetadata(image.Id, image.UploadedById, image.Visibility,
                image.ModerationStatus, image.DeletedAtUtc, image.StorageKey, image.ContentType,
                image.Embedding != null))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<ImageCard?> GetCardByIdAsync(int id, ImageViewer viewer, CancellationToken cancellationToken = default)
        => ImageCardQuery.Project(context.Images.AsNoTracking().Where(image => image.Id == id), viewer)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<(List<ImageCard> Images, int Total)> SearchAsync(SearchImageDto request, ImageViewer viewer,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var hasRandomSeed = RandomSortSeed.TryGetValue(request.RandomSeed, out var randomSeed);
        if (request.Sort == ImageSort.Random && !hasRandomSeed)
            throw new InvalidOperationException("A valid random sort seed is required.");
        var query = ImageQuery(false).Where(IsDiscoverable());
        query = request.Kind switch
        {
            ImageKind.Official => query.Where(image => image.Source == ImageSource.Scraped),
            ImageKind.Fan => query.Where(image => image.Source == ImageSource.UserUpload),
            _ => query
        };
        query = ApplyAiUsageFilter(query, request.AiUsage);

        query = ApplyTagFilters(query, request.Tags, request.ExcludedTags);

        query = ApplySort(query, request.Sort, randomSeed);
        var total = await query.CountAsync(cancellationToken);
        var images = await ReadPageAsync(query, page, pageSize, total, viewer, cancellationToken);
        return (images, total);
    }

    public async Task<(List<ImageCard> Images, int Total)> GetUploadedByUserAsync(int userId, bool includeHidden,
        int page, int pageSize, ImageViewer viewer, CancellationToken cancellationToken = default)
    {
        var query = ImageQuery(false).Where(image =>
            image.UploadedById == userId && (includeHidden || image.DeletedAtUtc == null));
        var total = await query.CountAsync(cancellationToken);
        var size = Math.Clamp(pageSize, 1, 50);
        var images = await ReadPageAsync(
            query.OrderByDescending(image => image.UploadedAtUtc).ThenByDescending(image => image.Id),
            page, size, total, viewer, cancellationToken);
        return (images, total);
    }

    public async Task<(List<ImageCard> Images, int Total)> GetLikedByUserAsync(int userId, int page, int pageSize,
        ImageViewer viewer, CancellationToken cancellationToken = default,
        IReadOnlyList<AiUsageClassification>? aiUsage = null)
    {
        var query = ImageQuery(false)
            .Where(image => image.DeletedAtUtc == null && image.Visibility == ImageVisibility.Gallery &&
                            image.ModerationStatus == ModerationStatus.Approved)
            .Where(image => image.Likes.Any(like => like.UserId == userId));
        query = ApplyAiUsageFilter(query, aiUsage);
        var total = await query.CountAsync(cancellationToken);
        var size = Math.Clamp(pageSize, 1, 50);
        var ordered = query.OrderByDescending(image => image.Likes
                .Where(like => like.UserId == userId).Select(like => like.CreatedAtUtc).Single())
            .ThenByDescending(image => image.Id);
        var images = await ReadPageAsync(ordered, page, size, total, viewer, cancellationToken);
        return (images, total);
    }

    public async Task<(List<ImageCard> Images, int Total)> GetRecommendationsAsync(int imageId, int page, int pageSize,
        ImageViewer viewer, CancellationToken cancellationToken = default,
        IReadOnlyList<AiUsageClassification>? aiUsage = null)
    {
        var embedding = await context.Images.AsNoTracking().Where(image => image.Id == imageId)
            .Select(image => image.Embedding).SingleOrDefaultAsync(cancellationToken);
        if (embedding is null)
            return ([], 0);
        var size = Math.Clamp(pageSize, 1, 50);
        var normalizedPage = Math.Max(page, 1);
        var query = ImageQuery(false).Where(IsDiscoverable())
            .Where(image => image.Id != imageId && image.Embedding != null);
        query = ApplyAiUsageFilter(query, aiUsage);
        query = query
            .OrderBy(image => image.Embedding!.L2Distance(embedding))
            .ThenBy(image => image.Id);
        var total = await query.CountAsync(cancellationToken);
        var images = await ReadPageAsync(query, normalizedPage, size, total, viewer, cancellationToken);
        return (images, total);
    }

    public Task<List<ImageCard>> GetPendingAsync(int page, int pageSize, ImageViewer viewer,
        CancellationToken cancellationToken = default)
        => ReadModerationPageAsync(ImageQuery(false)
                .Where(image => image.DeletedAtUtc == null && image.ModerationStatus == ModerationStatus.Pending)
                .OrderBy(image => image.UploadedAtUtc).ThenBy(image => image.Id), page, pageSize, viewer,
            cancellationToken);

    public Task<List<ImageCard>> GetHiddenAsync(int page, int pageSize, ImageViewer viewer,
        CancellationToken cancellationToken = default)
        => ReadModerationPageAsync(ImageQuery(false).Where(image => image.DeletedAtUtc != null)
                .OrderByDescending(image => image.DeletedAtUtc).ThenByDescending(image => image.Id),
            page, pageSize, viewer, cancellationToken);

    public Task<List<ImageTag>> GetByNormalizedNamesAsync(IEnumerable<string> normalizedTags,
        CancellationToken cancellationToken = default)
    {
        var names = normalizedTags.Distinct().ToArray();
        return context.Tags.Where(tag => names.Contains(tag.NormalizedName)).ToListAsync(cancellationToken);
    }

    public async Task<List<ImageTag>> GetOrCreateTagsAsync(IEnumerable<string> tagNames, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var cleaned = tagNames.Select(NormalizeTag).Where(tag => tag.Length > 0).Distinct().Take(20).ToArray();
        var existing = await GetByNormalizedNamesAsync(cleaned, cancellationToken);
        var known = existing.Select(tag => tag.NormalizedName).ToHashSet();
        var newTags = cleaned.Where(tag => known.Add(tag)).Select(tag => new ImageTag
        {
            Name = tag,
            NormalizedName = tag,
            CreatedAtUtc = now,
            ModerationStatus = TagModerationStatus.Pending
        }).ToList();
        if (newTags.Count > 0)
            await context.Tags.AddRangeAsync(newTags, cancellationToken);
        existing.AddRange(newTags);
        return existing;
    }

    public Task<List<ImageTag>> GetTagSuggestionsAsync(string normalizedQuery, int limit,
        CancellationToken cancellationToken = default)
        => context.Tags.AsNoTracking().Where(tag => tag.ModerationStatus == TagModerationStatus.Approved)
            .Where(tag => tag.AppImages.Any(image => image.DeletedAtUtc == null &&
                                                     image.Visibility == ImageVisibility.Gallery &&
                                                     image.ModerationStatus == ModerationStatus.Approved))
            .Where(tag => EF.Functions.ILike(tag.NormalizedName, $"{EscapeLike(normalizedQuery)}%", "\\"))
            .OrderByDescending(tag => tag.AppImages.Count(image => image.DeletedAtUtc == null &&
                                                                   image.Visibility == ImageVisibility.Gallery &&
                                                                   image.ModerationStatus == ModerationStatus.Approved))
            .ThenBy(tag => tag.Name).Take(Math.Clamp(limit, 1, 50)).ToListAsync(cancellationToken);

    public async Task<(List<ImageTag> Tags, int Total)> GetTagsAsync(TagModerationStatus status, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = context.Tags.AsNoTracking().Where(tag => tag.ModerationStatus == status)
            .OrderBy(tag => tag.CreatedAtUtc).ThenBy(tag => tag.Id);
        var total = await query.CountAsync(cancellationToken);
        var size = Math.Clamp(pageSize, 1, 50);
        var tags = await query.Include(tag => tag.AppImages).Skip(Math.Max(page - 1, 0) * size).Take(size)
            .ToListAsync(cancellationToken);
        return (tags, total);
    }

    public Task<ImageTag?> GetTagByIdAsync(int id, bool trackChanges, CancellationToken cancellationToken = default)
    {
        var query = trackChanges ? context.Tags : context.Tags.AsNoTracking();
        return query.Include(tag => tag.AppImages).ThenInclude(image => image.Tags)
            .SingleOrDefaultAsync(tag => tag.Id == id, cancellationToken);
    }

    public Task<List<AppImage>> GetByExternalMediaIdsAsync(IEnumerable<int> mediaIds, bool trackChanges,
        CancellationToken cancellationToken = default)
    {
        var ids = mediaIds.Distinct().ToArray();
        return ImageQuery(trackChanges).Include(image => image.Tags)
            .Where(image => image.Source == ImageSource.Scraped && image.ExternalMediaId != null &&
                            ids.Contains(image.ExternalMediaId.Value))
            .ToListAsync(cancellationToken);
    }

    public Task AddAsync(AppImage image, CancellationToken cancellationToken = default)
        => context.Images.AddAsync(image, cancellationToken).AsTask();

    public Task AddRangeAsync(IEnumerable<AppImage> images, CancellationToken cancellationToken = default)
        => context.Images.AddRangeAsync(images, cancellationToken);

    public async Task<List<int>> ClaimPendingEmbeddingsAsync(int batchSize, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var leaseExpires = now.AddMinutes(15);
        // UPDATE ... RETURNING cannot be composed by EF; enumerate the returned rows directly.
        var candidates = context.Images.FromSqlInterpolated($"""
                                                             UPDATE "Images" SET "EmbeddingStatus" = {"Processing"}, "EmbeddingAttempts" = "EmbeddingAttempts" + 1,
                                                                 "EmbeddingError" = NULL, "EmbeddingLeaseExpiresAtUtc" = {leaseExpires}
                                                             WHERE "Id" IN (
                                                                 SELECT "Id" FROM "Images"
                                                                 WHERE "DeletedAtUtc" IS NULL AND ("EmbeddingStatus" = {"Pending"} OR
                                                                     ("EmbeddingStatus" = {"Processing"} AND "EmbeddingLeaseExpiresAtUtc" < {now}))
                                                                 ORDER BY "UploadedAtUtc" FOR UPDATE SKIP LOCKED LIMIT {Math.Clamp(batchSize, 1, 50)}
                                                             ) RETURNING *
                                                             """).AsEnumerable().ToList();
        return candidates.Select(image => image.Id).ToList();
    }

    private IQueryable<AppImage> ImageQuery(bool trackChanges)
        => trackChanges ? context.Images : context.Images.AsNoTracking();

    private static Task<List<ImageCard>> ReadPageAsync(IQueryable<AppImage> query, int page, int pageSize,
        int total, ImageViewer viewer, CancellationToken cancellationToken)
    {
        var offset = ((long)Math.Max(page, 1) - 1) * pageSize;
        return offset >= total
            ? Task.FromResult(new List<ImageCard>())
            : ImageCardQuery.Project(query.Skip((int)offset).Take(pageSize), viewer).ToListAsync(cancellationToken);
    }

    private static Task<List<ImageCard>> ReadModerationPageAsync(IQueryable<AppImage> query, int page, int pageSize,
        ImageViewer viewer, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(pageSize, 1, 50);
        var offset = ((long)Math.Max(page, 1) - 1) * size;
        return offset > int.MaxValue
            ? Task.FromResult(new List<ImageCard>())
            : ImageCardQuery.Project(query.Skip((int)offset).Take(size), viewer).ToListAsync(cancellationToken);
    }

    private static System.Linq.Expressions.Expression<Func<AppImage, bool>> IsDiscoverable()
        => image => image.DeletedAtUtc == null && image.Visibility == ImageVisibility.Gallery &&
                    image.ModerationStatus == ModerationStatus.Approved;

    internal static IQueryable<AppImage> ApplySort(IQueryable<AppImage> query, ImageSort sort, long randomSeed)
        => sort switch
        {
            ImageSort.Oldest => query.OrderBy(image => image.UploadedAtUtc).ThenBy(image => image.Id),
            ImageSort.MediaId => query.OrderByDescending(image => image.ExternalMediaId)
                .ThenByDescending(image => image.UploadedAtUtc),
            ImageSort.Random => query.OrderBy(image => PostgreSqlHashFunctions.HashInt4Extended(image.Id, randomSeed))
                .ThenBy(image => image.Id),
            _ => query.OrderByDescending(image => image.UploadedAtUtc).ThenByDescending(image => image.Id)
        };

    private static IQueryable<AppImage> ApplyAiUsageFilter(IQueryable<AppImage> query,
        IReadOnlyList<AiUsageClassification>? aiUsage)
    {
        if (aiUsage is null || aiUsage.Count == 0)
            return query;
        var values = aiUsage.Distinct().ToArray();
        return query.Where(image => values.Contains(image.AiUsage));
    }

    internal static IQueryable<AppImage> ApplyTagFilters(IQueryable<AppImage> query,
        IReadOnlyList<string>? tags, IReadOnlyList<string>? excludedTags)
    {
        var normalizedTags = NormalizeTags(tags);
        var normalizedExcludedTags = NormalizeTags(excludedTags);

        if (normalizedTags.Count > 0)
            query = query.Where(image => normalizedTags.All(tag => image.Tags.Any(imageTag =>
                imageTag.NormalizedName == tag && imageTag.ModerationStatus == TagModerationStatus.Approved)));

        if (normalizedExcludedTags.Count > 0)
            query = query.Where(image => !image.Tags.Any(imageTag =>
                normalizedExcludedTags.Contains(imageTag.NormalizedName) &&
                imageTag.ModerationStatus == TagModerationStatus.Approved));

        return query;
    }

    private static List<string> NormalizeTags(IReadOnlyList<string>? tags)
        => tags?.Select(NormalizeTag).Where(tag => tag.Length > 0).Distinct().Take(20).ToList() ?? [];

    private static string NormalizeTag(string value) => value.Trim().ToLowerInvariant();

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
