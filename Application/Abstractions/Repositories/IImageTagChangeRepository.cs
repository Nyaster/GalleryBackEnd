using Entities.Models;

namespace Contracts;

public interface IImageTagChangeRepository
{
    Task<ImageTagChange?> GetByIdAsync(long id, bool trackChanges, CancellationToken cancellationToken = default);
    Task<(List<ImageTagChange> Changes, int Total)> GetAsync(int? imageId, ImageTagChangeStatus? status, int? editedByUserId,
        int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ImageTagChange?> GetPendingForImageAsync(int imageId, CancellationToken cancellationToken = default);
    Task<ImageTagChange?> GetLatestApprovedForImageAsync(int imageId, CancellationToken cancellationToken = default);
    Task<List<ImageTagChange>> GetPendingContainingTagAsync(string normalizedTag, CancellationToken cancellationToken = default);
    Task AddAsync(ImageTagChange change, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<ImageTagChange> changes, CancellationToken cancellationToken = default);
}
