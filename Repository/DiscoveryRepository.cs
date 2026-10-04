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

        var entries = images.Select(image => new
        {
            Image = image,
            PeriodLikeCount = collection == DiscoveryCollection.MostLiked
                ? image.Likes.Count(like => like.CreatedAtUtc >= startUtc && like.CreatedAtUtc < endUtc)
                : 0,
            PeriodCommentCount = collection == DiscoveryCollection.MostCommented
                ? image.Comments.Count(comment => comment.DeletedAtUtc == null &&
                                                  comment.CreatedAtUtc >= startUtc && comment.CreatedAtUtc < endUtc)
                : 0
        });
        var ordered = collection switch
        {
            DiscoveryCollection.MostLiked => entries.Where(entry => entry.PeriodLikeCount > 0)
                .OrderByDescending(entry => entry.PeriodLikeCount)
                .ThenByDescending(entry => entry.Image.UploadedAtUtc).ThenByDescending(entry => entry.Image.Id),
            DiscoveryCollection.MostCommented => entries.Where(entry => entry.PeriodCommentCount > 0)
                .OrderByDescending(entry => entry.PeriodCommentCount)
                .ThenByDescending(entry => entry.Image.UploadedAtUtc).ThenByDescending(entry => entry.Image.Id),
            DiscoveryCollection.NewUploads => entries.Where(entry => entry.Image.Source == ImageSource.UserUpload &&
                                                                     entry.Image.UploadedAtUtc >= startUtc &&
                                                                     entry.Image.UploadedAtUtc < endUtc)
                .OrderByDescending(entry => entry.Image.UploadedAtUtc).ThenByDescending(entry => entry.Image.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(collection))
        };
        var total = await ordered.CountAsync(cancellationToken);
        var offset = ((long)page - 1) * pageSize;
        IReadOnlyList<DiscoveryEntryDto> cards = [];
        if (offset < total)
        {
            cards = await ordered.Skip((int)offset).Take(pageSize).Select(entry => new DiscoveryEntryDto(
                    new AppImageDto(
                        entry.Image.Id,
                        entry.Image.Source,
                        entry.Image.UploadedBy == null ? null : entry.Image.UploadedBy.Login,
                        entry.Image.UploadedAtUtc,
                        "/api/images/" + entry.Image.Id + "/content",
                        entry.Image.Tags.Where(tag => tag.ModerationStatus == TagModerationStatus.Approved)
                            .OrderBy(tag => tag.Name).Select(tag => tag.Name).ToArray(),
                        entry.Image.Width,
                        entry.Image.Height,
                        entry.Image.Visibility,
                        entry.Image.ModerationStatus,
                        entry.Image.AiUsage,
                        null,
                        entry.Image.DeletedAtUtc,
                        entry.Image.Likes.Count,
                        entry.Image.Comments.Count(comment => comment.DeletedAtUtc == null),
                        entry.Image.Likes.Any(like => like.UserId == userId)),
                    collection == DiscoveryCollection.MostLiked ? entry.PeriodLikeCount : null,
                    collection == DiscoveryCollection.MostCommented ? entry.PeriodCommentCount : null))
                .ToListAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new DiscoveryPage(galleryTotal, total, cards);
    }
}