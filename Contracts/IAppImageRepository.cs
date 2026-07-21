using Entities.Models;
using Shared.DataTransferObjects;

namespace Contracts;

public interface IAppImageRepository
{
    Task<AppImage?> GetByIdAsync(int id, bool trackChanges, CancellationToken cancellationToken = default);
    Task<(List<AppImage> Images, int Total)> SearchAsync(SearchImageDto request, CancellationToken cancellationToken = default);
    Task<(List<AppImage> Images, int Total)> GetUploadedByUserAsync(int userId, bool includeHidden, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<(List<AppImage> Images, int Total)> GetLikedByUserAsync(int userId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<(List<AppImage> Images, int Total)> GetRecommendationsAsync(int imageId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<List<AppImage>> GetPendingAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<List<AppImage>> GetHiddenAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<List<ImageTag>> GetByNormalizedNamesAsync(IEnumerable<string> normalizedTags, CancellationToken cancellationToken = default);
    Task<List<ImageTag>> GetOrCreateTagsAsync(IEnumerable<string> tagNames, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<List<ImageTag>> GetTagSuggestionsAsync(string normalizedQuery, int limit, CancellationToken cancellationToken = default);
    Task<(List<ImageTag> Tags, int Total)> GetTagsAsync(TagModerationStatus status, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ImageTag?> GetTagByIdAsync(int id, bool trackChanges, CancellationToken cancellationToken = default);
    Task<List<AppImage>> GetByExternalMediaIdsAsync(IEnumerable<int> mediaIds, bool trackChanges, CancellationToken cancellationToken = default);
    Task AddAsync(AppImage image, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<AppImage> images, CancellationToken cancellationToken = default);
    Task<List<int>> ClaimPendingEmbeddingsAsync(int batchSize, DateTimeOffset now, CancellationToken cancellationToken = default);
}
