using Contracts;
using Entities.Models;
using Entities.Exceptions;
using Service.Contracts;

namespace Application.Helpers;

internal static class ImageAuthorization
{
    public static void EnsureReadable(AppImage image, IUserContext currentUser)
        => EnsureReadable(image.UploadedById, image.Visibility, image.ModerationStatus, image.DeletedAtUtc, currentUser);

    public static void EnsureReadable(ImageMetadata image, IUserContext currentUser)
    {
        if (currentUser.UserId is null)
        {
            if (!image.IsGuestVisible) throw new Base404ReturnException("Image not found.");
            return;
        }

        EnsureReadable(image.UploadedById, image.Visibility, image.ModerationStatus, image.DeletedAtUtc, currentUser);
    }

    public static void EnsureReadable(ImageCard image, IUserContext currentUser)
    {
        if (currentUser.UserId is null)
        {
            if (!image.IsGuestVisible) throw new Base404ReturnException("Image not found.");
            return;
        }

        EnsureReadable(image.UploadedById, image.Visibility, image.ModerationStatus, image.DeletedAtUtc, currentUser);
    }

    public static async Task<ImageMetadata> GetReadableMetadataAsync(IAppImageRepository repository, int id,
        IUserContext currentUser, CancellationToken cancellationToken)
    {
        var image = await (currentUser.UserId is null
            ? repository.GetGuestMetadataByIdAsync(id, cancellationToken)
            : repository.GetMetadataByIdAsync(id, cancellationToken))
            ?? throw new Base404ReturnException("Image not found.");
        EnsureReadable(image, currentUser);
        return image;
    }

    private static void EnsureReadable(int? uploadedById, ImageVisibility visibility, ModerationStatus moderationStatus,
        DateTimeOffset? deletedAtUtc, IUserContext currentUser)
    {
        currentUser.RequireAuthenticated();
        var canManage = IsStaff(currentUser) || (currentUser.UserId is not null && uploadedById == currentUser.UserId);
        if (deletedAtUtc is not null)
        {
            if (canManage) return;
            throw new Base404ReturnException("Image not found.");
        }

        if (canManage)
            return;
        if (visibility == ImageVisibility.Gallery && moderationStatus == ModerationStatus.Approved)
            return;
        throw new Entities.Exceptions.AppForbiddenException("You do not have permission to access this image.");
    }

    public static void EnsureCanManage(AppImage image, IUserContext currentUser)
    {
        currentUser.RequireAuthenticated();
        if (CanManage(image, currentUser)) return;
        throw new AppForbiddenException("You do not have permission to manage this image.");
    }

    public static void EnsureCanManage(ImageMetadata image, IUserContext currentUser)
    {
        currentUser.RequireAuthenticated();
        if (IsStaff(currentUser) || (currentUser.UserId is not null && image.UploadedById == currentUser.UserId)) return;
        throw new AppForbiddenException("You do not have permission to manage this image.");
    }

    public static bool IsStaff(IUserContext currentUser)
        => currentUser.IsInRole(nameof(AppUserRole.Admin)) || currentUser.IsInRole(nameof(AppUserRole.Moderator));

    public static ImageViewer GetViewer(IUserContext currentUser) => new(currentUser.UserId, IsStaff(currentUser));

    public static bool CanManage(AppImage image, IUserContext currentUser)
        => IsStaff(currentUser) || (currentUser.UserId is not null && image.UploadedById == currentUser.UserId);
}
