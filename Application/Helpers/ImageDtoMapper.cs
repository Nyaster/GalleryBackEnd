using Contracts;
using Entities.Models;
using Shared.DataTransferObjects;

namespace Application.Helpers;

internal static class ImageDtoMapper
{
    public static AppImageDto ToDto(ImageCard image)
        => new(image.Id, image.Source, image.UploadedBy, image.UploadedAtUtc,
            $"/api/images/{image.Id}/content",
            image.Tags.Where(tag => tag.ModerationStatus == TagModerationStatus.Approved)
                .OrderBy(tag => tag.Name).Select(tag => tag.Name).ToArray(),
            image.Width, image.Height, image.Visibility, image.ModerationStatus, image.AiUsage,
            image.CanSeePendingTags
                ? image.Tags.Where(tag => tag.ModerationStatus == TagModerationStatus.Pending)
                    .OrderBy(tag => tag.Name).Select(tag => tag.Name).ToArray()
                : null,
            image.DeletedAtUtc, image.LikeCount, image.CommentCount, image.IsLikedByCurrentUser);
}
