using Contracts;
using Entities.Models;
using Microsoft.EntityFrameworkCore;

namespace Repository;

public sealed class ImageTagChangeRepository(RepositoryContext context) : IImageTagChangeRepository
{
    public Task<ImageTagChange?> GetByIdAsync(long id, bool trackChanges, CancellationToken cancellationToken = default)
        => (trackChanges ? context.ImageTagChanges : context.ImageTagChanges.AsNoTracking())
            .SingleOrDefaultAsync(change => change.Id == id, cancellationToken);

    public async Task<(List<ImageTagChange> Changes, int Total)> GetAsync(int? imageId, ImageTagChangeStatus? status,
        int? editedByUserId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = context.ImageTagChanges.AsNoTracking();
        if (imageId is int value) query = query.Where(change => change.ImageId == value);
        if (status is ImageTagChangeStatus requestedStatus) query = query.Where(change => change.Status == requestedStatus);
        if (editedByUserId is int editorId) query = query.Where(change => change.EditedByUserId == editorId);
        var total = await query.CountAsync(cancellationToken);
        var size = Math.Clamp(pageSize, 1, 50);
        var ordered = status == ImageTagChangeStatus.Pending
            ? query.OrderBy(change => change.CreatedAtUtc).ThenBy(change => change.Id)
            : query.OrderByDescending(change => change.CreatedAtUtc).ThenByDescending(change => change.Id);
        return (await ordered.Skip((Math.Max(page, 1) - 1) * size).Take(size).ToListAsync(cancellationToken), total);
    }

    public Task<ImageTagChange?> GetPendingForImageAsync(int imageId, CancellationToken cancellationToken = default)
        => context.ImageTagChanges.SingleOrDefaultAsync(change => change.ImageId == imageId &&
            change.Status == ImageTagChangeStatus.Pending, cancellationToken);

    public Task<ImageTagChange?> GetLatestApprovedForImageAsync(int imageId, CancellationToken cancellationToken = default)
        => context.ImageTagChanges.Where(change => change.ImageId == imageId && change.Status == ImageTagChangeStatus.Approved)
            .OrderByDescending(change => change.AppliedAtUtc).ThenByDescending(change => change.Id).FirstOrDefaultAsync(cancellationToken);

    public Task<List<ImageTagChange>> GetPendingContainingTagAsync(string normalizedTag, CancellationToken cancellationToken = default)
        => context.ImageTagChanges.Where(change => change.Status == ImageTagChangeStatus.Pending &&
            (change.PreviousTags.Contains(normalizedTag) || change.ProposedTags.Contains(normalizedTag))).ToListAsync(cancellationToken);

    public Task AddAsync(ImageTagChange change, CancellationToken cancellationToken = default)
        => context.ImageTagChanges.AddAsync(change, cancellationToken).AsTask();

    public Task AddRangeAsync(IEnumerable<ImageTagChange> changes, CancellationToken cancellationToken = default)
        => context.ImageTagChanges.AddRangeAsync(changes, cancellationToken);
}
