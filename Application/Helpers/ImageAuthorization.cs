using Entities.Models;
using Entities.Exceptions;
using Service.Contracts;

namespace Application.Helpers;

internal static class ImageAuthorization
{
    public static void EnsureReadable(AppImage image, IUserContext currentUser)
    {
        currentUser.RequireAuthenticated();
        if (image.DeletedAtUtc is not null)
        {
            if (CanManage(image, currentUser)) return;
            throw new Base404ReturnException("Image not found.");
        }
        if (CanManage(image, currentUser))
            return;
        if (image.Visibility == ImageVisibility.Gallery && image.ModerationStatus == ModerationStatus.Approved)
            return;
        throw new Entities.Exceptions.AppForbiddenException("You do not have permission to access this image.");
    }

    public static void EnsureCanManage(AppImage image, IUserContext currentUser)
    {
        currentUser.RequireAuthenticated();
        if (CanManage(image, currentUser)) return;
        throw new AppForbiddenException("You do not have permission to manage this image.");
    }

    public static bool IsStaff(IUserContext currentUser)
        => currentUser.IsInRole(nameof(AppUserRole.Admin)) || currentUser.IsInRole(nameof(AppUserRole.Moderator));

    public static bool CanManage(AppImage image, IUserContext currentUser)
        => IsStaff(currentUser) || image.UploadedById == currentUser.UserId;
}
