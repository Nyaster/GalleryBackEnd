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
        canSeePendingTags ? image.Tags.Where(tag => tag.ModerationStatus == TagModerationStatus.Pending).OrderBy(tag => tag.Name).Select(tag => tag.Name).ToArray() : null,
        image.DeletedAtUtc);
    }
}
