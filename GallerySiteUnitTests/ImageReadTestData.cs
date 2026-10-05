using Contracts;
using Entities.Models;

namespace GallerySiteUnitTests;

internal static class ImageReadTestData
{
    public static ImageMetadata Metadata(AppImage image)
        => new(image.Id, image.UploadedById, image.Visibility, image.ModerationStatus, image.DeletedAtUtc,
            image.StorageKey, image.ContentType, image.Embedding != null);

    public static ImageCard Card(AppImage image, ImageViewer? viewer = null)
    {
        var reader = viewer ?? new ImageViewer(7, false);
        var canManage = reader.IsStaff || image.UploadedById == reader.UserId;
        return new ImageCard(image.Id, image.Source, image.UploadedById, image.UploadedBy?.Login,
            image.UploadedAtUtc, image.Width, image.Height, image.Visibility, image.ModerationStatus, image.AiUsage,
            image.DeletedAtUtc, image.Tags.OrderBy(tag => tag.Name)
                .Select(tag => new ImageTagSummary(tag.Name, tag.ModerationStatus)).ToArray(),
            image.Likes.Count, image.Comments.Count(comment => comment.DeletedAtUtc == null),
            image.Likes.Any(like => like.UserId == reader.UserId), canManage);
    }
}
