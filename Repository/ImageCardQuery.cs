using Contracts;
using Entities.Models;

namespace Repository;

internal static class ImageCardQuery
{
    public static IQueryable<ImageCard> Project(IQueryable<AppImage> images, ImageViewer viewer)
        => images.Select(image => new ImageCard(
            image.Id, image.Source, image.UploadedById,
            image.UploadedBy == null ? null : image.UploadedBy.Login,
            image.UploadedAtUtc, image.Width, image.Height, image.Visibility, image.ModerationStatus,
            image.AiUsage, image.DeletedAtUtc,
            image.Tags.Where(tag => tag.ModerationStatus == TagModerationStatus.Approved ||
                                    ((viewer.IsStaff ||
                                      (viewer.UserId != null && image.UploadedById == viewer.UserId)) &&
                                     tag.ModerationStatus == TagModerationStatus.Pending))
                .OrderBy(tag => tag.Name).Select(tag => new ImageTagSummary(tag.Name, tag.ModerationStatus)).ToArray(),
            image.Likes.Count, image.Comments.Count(comment => comment.DeletedAtUtc == null),
            viewer.UserId != null && image.Likes.Any(like => like.UserId == viewer.UserId),
            viewer.IsStaff || (viewer.UserId != null && image.UploadedById == viewer.UserId)));
}
