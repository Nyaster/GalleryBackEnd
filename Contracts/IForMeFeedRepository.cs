using Entities.Models;

namespace Contracts;

public sealed record ForMeFilters(int[] AiUsage, string[] ExcludedTags);

public sealed record ForMeFeedPage(ForMeFeed Feed, IReadOnlyList<ImageCard> Images, int LastRank, bool HasMore);

public interface IForMeFeedRepository
{
    Task<Guid> CreateAsync(int userId, int pageSize, ForMeFilters filters,
        CancellationToken cancellationToken = default);
    Task<ForMeFeedPage> ReadAsync(Guid feedId, ImageViewer viewer, int afterRank,
        CancellationToken cancellationToken = default);
    Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default);
}
