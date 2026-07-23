using Entities.Models;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Helpers;

internal static class ImageDtoMapper
{
    public static AppImageDto ToDto(AppImage image, IUserContext? currentUser = null)
    {
        var canSeePendingTags = currentUser is not null && ImageAuthorization.CanManage(image, currentUser);
        return new AppImageDto(
        image.Id,
        image.Source,
        image.UploadedBy?.Login,
        image.UploadedAtUtc,
        $"/api/images/{image.Id}/content",
        image.Tags.Where(tag => tag.ModerationStatus == TagModerationStatus.Approved).OrderBy(tag => tag.Name).Select(tag => tag.Name).ToArray(),
        image.Width,
        image.Height,
        image.Visibility,
        image.ModerationStatus,
        image.AiUsage,
        canSeePendingTags ? image.Tags.Where(tag => tag.ModerationStatus == TagModerationStatus.Pending).OrderBy(tag => tag.Name).Select(tag => tag.Name).ToArray() : null,
        image.DeletedAtUtc,
        image.Likes.Count,
        image.Comments.Count(comment => comment.DeletedAtUtc is null),
        currentUser?.UserId is int userId && image.Likes.Any(like => like.UserId == userId));
    }
}
