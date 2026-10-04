using Entities.Models;
using Shared.DataTransferObjects;

namespace Contracts;

public sealed record DiscoveryPage(int GalleryTotal, int Total, IReadOnlyList<DiscoveryEntryDto> Entries);

public interface IDiscoveryRepository
{
    Task<DiscoveryPage> GetCollectionAsync(DiscoveryCollection collection, DateTimeOffset startUtc,
        DateTimeOffset endUtc, int userId, int page, int pageSize,
        IReadOnlyList<AiUsageClassification>? aiUsage = null, CancellationToken cancellationToken = default);
}
