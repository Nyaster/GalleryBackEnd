using Entities.Models;
using Service.Contracts;

namespace Application.Helpers;

internal static class ImageAuthorization
{
    public static void EnsureReadable(AppImage image, IUserContext currentUser)
    {
        currentUser.RequireAuthenticated();
        if (image.DeletedAtUtc is not null)
            throw new InvalidOperationException("Deleted images are not available.");
        if (currentUser.IsInRole(nameof(AppUserRole.Admin)) || image.UploadedById == currentUser.UserId)
            return;
        if (image.Visibility == ImageVisibility.Gallery && image.ModerationStatus == ModerationStatus.Approved)
            return;
        throw new Entities.Exceptions.AppForbiddenException("You do not have permission to access this image.");
    }
}
