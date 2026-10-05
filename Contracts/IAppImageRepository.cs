using Entities.Models;
using Shared.DataTransferObjects;

namespace Contracts;

public interface IAppImageRepository
{
    Task<AppImage?> GetByIdAsync(int id, bool trackChanges, CancellationToken cancellationToken = default);
    Task<AppImage?> GetWithTagsByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ImageMetadata?> GetMetadataByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ImageCard?> GetCardByIdAsync(int id, ImageViewer viewer, CancellationToken cancellationToken = default);

    Task<(List<ImageCard> Images, int Total)> SearchAsync(SearchImageDto request, ImageViewer viewer,
        CancellationToken cancellationToken = default);

    Task<(List<ImageCard> Images, int Total)> GetUploadedByUserAsync(int userId, bool includeHidden, int page,
        int pageSize, ImageViewer viewer, CancellationToken cancellationToken = default);

    Task<(List<ImageCard> Images, int Total)> GetLikedByUserAsync(int userId, int page, int pageSize,
        ImageViewer viewer, CancellationToken cancellationToken = default,
        IReadOnlyList<AiUsageClassification>? aiUsage = null);

    Task<(List<ImageCard> Images, int Total)> GetRecommendationsAsync(int imageId, int page, int pageSize,
        ImageViewer viewer, CancellationToken cancellationToken = default,
        IReadOnlyList<AiUsageClassification>? aiUsage = null);

    Task<List<ImageCard>> GetPendingAsync(int page, int pageSize, ImageViewer viewer,
        CancellationToken cancellationToken = default);

    Task<List<ImageCard>> GetHiddenAsync(int page, int pageSize, ImageViewer viewer,
        CancellationToken cancellationToken = default);

    Task<List<ImageTag>> GetByNormalizedNamesAsync(IEnumerable<string> normalizedTags,
        CancellationToken cancellationToken = default);

    Task<List<ImageTag>> GetOrCreateTagsAsync(IEnumerable<string> tagNames, DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<List<ImageTag>> GetTagSuggestionsAsync(string normalizedQuery, int limit,
        CancellationToken cancellationToken = default);

    Task<(List<ImageTag> Tags, int Total)> GetTagsAsync(TagModerationStatus status, int page, int pageSize,
        CancellationToken cancellationToken = default);

    Task<ImageTag?> GetTagByIdAsync(int id, bool trackChanges, CancellationToken cancellationToken = default);

    Task<List<AppImage>> GetByExternalMediaIdsAsync(IEnumerable<int> mediaIds, bool trackChanges,
        CancellationToken cancellationToken = default);

    Task AddAsync(AppImage image, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<AppImage> images, CancellationToken cancellationToken = default);

    Task<List<int>> ClaimPendingEmbeddingsAsync(int batchSize, DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
