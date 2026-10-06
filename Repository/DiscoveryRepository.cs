using System.Data;
using Contracts;
using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Shared.DataTransferObjects;

namespace Repository;

public sealed class DiscoveryRepository(RepositoryContext context) : IDiscoveryRepository
{
    public async Task<DiscoveryPage> GetCollectionAsync(DiscoveryCollection collection, DateTimeOffset startUtc,
        DateTimeOffset endUtc, int userId, int page, int pageSize,
        IReadOnlyList<AiUsageClassification>? aiUsage = null, CancellationToken cancellationToken = default)
    {
        // PostgreSQL repeatable read keeps totals, metrics and cards on the same snapshot.
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,
            cancellationToken);
        var images = context.Images.AsNoTracking().Where(image => image.DeletedAtUtc == null &&
                                                                  image.Visibility == ImageVisibility.Gallery &&
                                                                  image.ModerationStatus == ModerationStatus.Approved);
        var galleryTotal = await images.CountAsync(cancellationToken);
        if (aiUsage is { Count: > 0 })
        {
            var classifications = aiUsage.Distinct().ToArray();
            images = images.Where(image => Enumerable.Contains(classifications, image.AiUsage));
        }

        // Aggregate the day's interactions once instead of counting them for every gallery image.
        var entries = collection switch
        {
            DiscoveryCollection.MostLiked => context.ImageLikes.AsNoTracking()
                .Where(like => like.CreatedAtUtc >= startUtc && like.CreatedAtUtc < endUtc)
                .GroupBy(like => like.ImageId)
                .Select(group => new { ImageId = group.Key, Count = group.Count() })
                .Join(images, period => period.ImageId, image => image.Id, (period, image) => new DiscoveryRow
                {
                    ImageId = image.Id, UploadedAtUtc = image.UploadedAtUtc, PeriodLikeCount = period.Count
                }),
            DiscoveryCollection.MostCommented => context.Comments.AsNoTracking()
                .Where(comment => comment.DeletedAtUtc == null && comment.CreatedAtUtc >= startUtc &&
                                  comment.CreatedAtUtc < endUtc)
                .GroupBy(comment => comment.ImageId)
                .Select(group => new { ImageId = group.Key, Count = group.Count() })
                .Join(images, period => period.ImageId, image => image.Id, (period, image) => new DiscoveryRow
                {
                    ImageId = image.Id, UploadedAtUtc = image.UploadedAtUtc, PeriodCommentCount = period.Count
                }),
            DiscoveryCollection.NewUploads => images.Where(image => image.Source == ImageSource.UserUpload &&
                                                                    image.UploadedAtUtc >= startUtc &&
                                                                    image.UploadedAtUtc < endUtc)
                .Select(image => new DiscoveryRow { ImageId = image.Id, UploadedAtUtc = image.UploadedAtUtc }),
            _ => throw new ArgumentOutOfRangeException(nameof(collection))
        };
        var total = await entries.CountAsync(cancellationToken);
        var offset = ((long)page - 1) * pageSize;
        IReadOnlyList<DiscoveryEntryDto> cards = [];
        if (offset < total)
        {
            var ordered = collection switch
            {
                DiscoveryCollection.MostLiked => entries.OrderByDescending(entry => entry.PeriodLikeCount),
                DiscoveryCollection.MostCommented => entries.OrderByDescending(entry => entry.PeriodCommentCount),
                _ => entries.OrderByDescending(entry => entry.UploadedAtUtc)
            };
            var rows = await ordered.ThenByDescending(entry => entry.UploadedAtUtc)
                .ThenByDescending(entry => entry.ImageId).Skip((int)offset).Take(pageSize)
                .ToListAsync(cancellationToken);
            // Keep tags, all-time counts and viewer state out of the ranking query.
            var ids = rows.Select(row => row.ImageId).ToArray();
            var imagesById = await context.Images.AsNoTracking().Where(image => Enumerable.Contains(ids, image.Id))
                .Select(image => new AppImageDto(
                    image.Id,
                    image.Source,
                    image.UploadedBy == null ? null : image.UploadedBy.Login,
                    image.UploadedAtUtc,
                    "/api/images/" + image.Id + "/content",
                    image.Tags.Where(tag => tag.ModerationStatus == TagModerationStatus.Approved)
                        .OrderBy(tag => tag.Name).Select(tag => tag.Name).ToArray(),
                    image.Width,
                    image.Height,
                    image.Visibility,
                    image.ModerationStatus,
                    image.AiUsage,
                    null,
                    image.DeletedAtUtc,
                    image.Likes.Count,
                    image.Comments.Count(comment => comment.DeletedAtUtc == null),
                    image.Likes.Any(like => like.UserId == userId)))
                .ToDictionaryAsync(image => image.Id, cancellationToken);
            cards = rows.Select(row => new DiscoveryEntryDto(imagesById[row.ImageId],
                row.PeriodLikeCount, row.PeriodCommentCount)).ToArray();
        }

        await transaction.CommitAsync(cancellationToken);
        return new DiscoveryPage(galleryTotal, total, cards);
    }

    private sealed class DiscoveryRow
    {
        public int ImageId { get; set; }
        public DateTimeOffset UploadedAtUtc { get; set; }
        public int? PeriodLikeCount { get; set; }
        public int? PeriodCommentCount { get; set; }
    }
}