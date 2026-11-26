using Shared.DataTransferObjects;

namespace Contracts;

public sealed record ImageViewBatch(
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyDictionary<int, long> Totals,
    IReadOnlyDictionary<(int ImageId, int UserId), DateTimeOffset> FirstSeen);

public interface IImageViewRepository
{
    Task ApplyAsync(ImageViewBatch batch, CancellationToken cancellationToken);
    Task<ImageViewTotals> GetTotalsAsync(int imageId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ImageViewDay>> GetDailyAsync(int imageId, DateOnly from, DateOnly through,
        CancellationToken cancellationToken);
}
