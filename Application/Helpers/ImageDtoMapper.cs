using Contracts;
using Entities.Models;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Helpers;

internal static class ImageDtoMapper
{
    public static AppImageDto ToDto(ImageCard image, IUserContext user) => ToDto(image, user, "/api/images");

    public static AppImageDto ToModerationDto(ImageCard image, IUserContext user) =>
        ToDto(image, user, "/api/admin/images");

    private static AppImageDto ToDto(ImageCard image, IUserContext user, string basePath)
    {
        var isOwner = ImageAuthorization.IsOwner(image.UploadedById, user);
        var isHidden = image.DeletedAtUtc is not null;
        return new AppImageDto(image.Id, image.Source, image.UploadedBy, image.UploadedAtUtc,
            $"{basePath}/{image.Id}/content",
            image.Tags.Where(tag => tag.ModerationStatus == TagModerationStatus.Approved)
                .OrderBy(tag => tag.Name).Select(tag => tag.Name).ToArray(),
            image.Width, image.Height, image.Visibility, image.ModerationStatus, image.AiUsage,
            image.CanSeePendingTags
                ? image.Tags.Where(tag => tag.ModerationStatus == TagModerationStatus.Pending)
                    .OrderBy(tag => tag.Name).Select(tag => tag.Name).ToArray()
                : null,
            image.DeletedAtUtc, image.LikeCount, image.CommentCount, image.IsLikedByCurrentUser,
            !isHidden && (isOwner || user.HasPermission(AccessPermission.ImagesHideAny)),
            isHidden && (isOwner || user.HasPermission(AccessPermission.ImagesRestoreAny)),
            image.Visibility == ImageVisibility.Private &&
            (isOwner || user.HasPermission(AccessPermission.ImagesPublishAny)),
            isOwner || user.HasPermission(AccessPermission.ImageTagsEditAny),
            isOwner || user.HasPermission(AccessPermission.ImageTagsReview));
    }
}