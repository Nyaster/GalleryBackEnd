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

    public async Task<(List<AppImage> Images, int Total)> SearchAsync(SearchImageDto request, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var query = ImageQuery(false).Where(IsDiscoverable());
        query = request.Kind switch
        {
            ImageKind.Official => query.Where(image => image.Source == ImageSource.Scraped),
            ImageKind.Fan => query.Where(image => image.Source == ImageSource.UserUpload),
            _ => query
        };

        var normalizedTags = NormalizeTags(request.Tags);
        if (normalizedTags.Count > 0)
            query = query.Where(image => normalizedTags.All(tag => image.Tags.Any(imageTag => imageTag.NormalizedName == tag)));

        query = request.Sort switch
        {
            ImageSort.Oldest => query.OrderBy(image => image.UploadedAtUtc).ThenBy(image => image.Id),
            ImageSort.MediaId => query.OrderByDescending(image => image.ExternalMediaId).ThenByDescending(image => image.UploadedAtUtc),
            _ => query.OrderByDescending(image => image.UploadedAtUtc).ThenByDescending(image => image.Id)
        };
        var total = await query.CountAsync(cancellationToken);
        var images = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (images, total);
    }

    public Task<List<AppImage>> GetUploadedByUserAsync(int userId, CancellationToken cancellationToken = default)
        => ImageQuery(false).Where(image => image.UploadedById == userId && image.DeletedAtUtc == null)
            .OrderByDescending(image => image.UploadedAtUtc).ToListAsync(cancellationToken);

    public async Task<List<AppImage>> GetRecommendationsAsync(int imageId, int limit, CancellationToken cancellationToken = default)
    {
        var source = await context.Images.AsNoTracking().SingleOrDefaultAsync(image => image.Id == imageId, cancellationToken);
        if (source?.Embedding is null)
            return [];
        return await ImageQuery(false).Where(IsDiscoverable())
            .Where(image => image.Id != imageId && image.Embedding != null)
            .OrderBy(image => image.Embedding!.L2Distance(source.Embedding))
            .Take(Math.Clamp(limit, 1, 50)).ToListAsync(cancellationToken);
    }

    public Task<List<AppImage>> GetPendingAsync(int page, int pageSize, CancellationToken cancellationToken = default)
        => ImageQuery(false).Where(image => image.DeletedAtUtc == null && image.ModerationStatus == ModerationStatus.Pending)
            .OrderBy(image => image.UploadedAtUtc).Skip(Math.Max(page - 1, 0) * Math.Clamp(pageSize, 1, 50))
            .Take(Math.Clamp(pageSize, 1, 50)).ToListAsync(cancellationToken);

    public Task<List<ImageTag>> GetByNormalizedNamesAsync(IEnumerable<string> normalizedTags, CancellationToken cancellationToken = default)
    {
        var names = normalizedTags.Distinct().ToArray();
        return context.Tags.Where(tag => names.Contains(tag.NormalizedName)).ToListAsync(cancellationToken);
    }

    public async Task<List<ImageTag>> GetOrCreateTagsAsync(IEnumerable<string> tagNames, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var cleaned = tagNames.Select(NormalizeTag).Where(tag => tag.Length > 0).Distinct().Take(20).ToArray();
        var existing = await GetByNormalizedNamesAsync(cleaned, cancellationToken);
        var known = existing.Select(tag => tag.NormalizedName).ToHashSet();
        var newTags = cleaned.Where(tag => known.Add(tag)).Select(tag => new ImageTag
        {
            Name = tag,
            NormalizedName = tag,
            CreatedAtUtc = now
        }).ToList();
        if (newTags.Count > 0)
            await context.Tags.AddRangeAsync(newTags, cancellationToken);
        existing.AddRange(newTags);
        return existing;
    }

    public Task<List<ImageTag>> GetTagSuggestionsAsync(string normalizedQuery, int limit, CancellationToken cancellationToken = default)
        => context.Tags.AsNoTracking().Where(tag => tag.AppImages.Any(image => image.DeletedAtUtc == null &&
                image.Visibility == ImageVisibility.Gallery && image.ModerationStatus == ModerationStatus.Approved))
            .Where(tag => EF.Functions.ILike(tag.NormalizedName, $"{EscapeLike(normalizedQuery)}%", "\\"))
            .OrderBy(tag => tag.Name).Take(Math.Clamp(limit, 1, 50)).ToListAsync(cancellationToken);

    public Task<List<AppImage>> GetByExternalMediaIdsAsync(IEnumerable<int> mediaIds, bool trackChanges, CancellationToken cancellationToken = default)
    {
        var ids = mediaIds.Distinct().ToArray();
        return ImageQuery(trackChanges).Where(image => image.Source == ImageSource.Scraped && image.ExternalMediaId != null && ids.Contains(image.ExternalMediaId.Value))
            .ToListAsync(cancellationToken);
    }

    public Task AddAsync(AppImage image, CancellationToken cancellationToken = default)
        => context.Images.AddAsync(image, cancellationToken).AsTask();

    public Task AddRangeAsync(IEnumerable<AppImage> images, CancellationToken cancellationToken = default)
        => context.Images.AddRangeAsync(images, cancellationToken);

    public async Task<List<int>> ClaimPendingEmbeddingsAsync(int batchSize, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var candidates = await context.Images.Where(image => image.EmbeddingStatus == EmbeddingStatus.Pending && image.DeletedAtUtc == null)
            .OrderBy(image => image.UploadedAtUtc).Take(Math.Clamp(batchSize, 1, 50)).ToListAsync(cancellationToken);
        foreach (var image in candidates)
        {
            image.EmbeddingStatus = EmbeddingStatus.Processing;
            image.EmbeddingAttempts++;
            image.EmbeddingError = null;
        }
        await context.SaveChangesAsync(cancellationToken);
        return candidates.Select(image => image.Id).ToList();
    }

    private IQueryable<AppImage> ImageQuery(bool trackChanges)
    {
        var query = trackChanges ? context.Images : context.Images.AsNoTracking();
        return query.Include(image => image.Tags).Include(image => image.UploadedBy);
    }

    private static System.Linq.Expressions.Expression<Func<AppImage, bool>> IsDiscoverable()
        => image => image.DeletedAtUtc == null && image.Visibility == ImageVisibility.Gallery && image.ModerationStatus == ModerationStatus.Approved;

    private static List<string> NormalizeTags(IReadOnlyList<string>? tags)
        => tags?.Select(NormalizeTag).Where(tag => tag.Length > 0).Distinct().Take(20).ToList() ?? [];

    private static string NormalizeTag(string value) => value.Trim().ToLowerInvariant();
    private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
