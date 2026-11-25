using Contracts;
using Entities.Exceptions;
using Entities.Models;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Helpers;

internal static class ImageAuthorization
{
    public static void EnsureReadable(AppImage image, IUserContext currentUser)
        => EnsureReadable(image.UploadedById, image.Visibility, image.ModerationStatus, image.DeletedAtUtc,
            currentUser);

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
        var image = await repository.GetReadableMetadataByIdAsync(id, GetViewer(currentUser), cancellationToken)
                    ?? throw new Base404ReturnException("Image not found.");
        EnsureReadable(image, currentUser);
        return image;
    }

    public static async Task<ImageMetadata> GetPublicMetadataAsync(IAppImageRepository repository, int id,
        IUserContext currentUser, CancellationToken cancellationToken)
    {
        var viewer = GetViewer(currentUser);
        var image = await repository.GetReadableMetadataByIdAsync(id, viewer, cancellationToken);
        if (image is null || (viewer.UserId is null && !image.IsGuestVisible) || image.DeletedAtUtc is not null ||
            image.Visibility != ImageVisibility.Gallery || image.ModerationStatus != ModerationStatus.Approved)
            throw new Base404ReturnException("Image not found.");
        return image;
    }

    private static void EnsureReadable(int? uploadedById, ImageVisibility visibility, ModerationStatus moderationStatus,
        DateTimeOffset? deletedAtUtc, IUserContext currentUser)
    {
        currentUser.RequireAuthenticated();
        var canReadRestricted = currentUser.HasPermission(AccessPermission.ImagesReadRestricted) ||
                                IsOwner(uploadedById, currentUser);
        if (deletedAtUtc is not null)
        {
            if (canReadRestricted) return;
            throw new Base404ReturnException("Image not found.");
        }

        if (canReadRestricted)
            return;
        if (visibility == ImageVisibility.Gallery && moderationStatus == ModerationStatus.Approved)
            return;
        throw new AppForbiddenException("You do not have permission to access this image.");
    }

    public static void EnsureReadableForManagement(AppImage image, IUserContext currentUser)
    {
        if (!CanReview(image.ModerationStatus, image.DeletedAtUtc, currentUser))
            EnsureReadable(image, currentUser);
    }

    public static async Task<AppImageDto?> GetManagementMutationResponseAsync(IAppImageRepository repository,
        AppImage image, IUserContext currentUser, CancellationToken cancellationToken)
    {
        var isOwner = IsOwner(image.UploadedById, currentUser);
        var canReview = CanReview(image.ModerationStatus, image.DeletedAtUtc, currentUser);
        var canReadRestricted = currentUser.HasPermission(AccessPermission.ImagesReadRestricted);
        var canReadOrdinary = isOwner || (image.DeletedAtUtc is null &&
                                          image.Visibility == ImageVisibility.Gallery &&
                                          image.ModerationStatus == ModerationStatus.Approved);

        if (canReadOrdinary && (isOwner || (!canReview && !canReadRestricted)))
        {
            var card = await repository.GetCardByIdAsync(image.Id, GetViewer(currentUser), cancellationToken);
            if (card is not null)
                return ImageDtoMapper.ToDto(card, currentUser);
        }

        if (!canReview && !canReadRestricted)
            return null;

        var managementCard = await repository.GetManagementCardByIdAsync(image.Id,
            GetViewer(currentUser, includeReviewTags: canReview), cancellationToken);
        return managementCard is null ? null : ImageDtoMapper.ToModerationDto(managementCard, currentUser);
    }

    public static bool CanReview(ModerationStatus status, DateTimeOffset? hiddenAt, IUserContext currentUser)
        => currentUser.HasPermission(AccessPermission.ImagesReviewQueue) &&
           (hiddenAt is not null || status == ModerationStatus.Pending);

    public static void EnsureOwnerOrPermission(int? uploadedById, IUserContext currentUser, string permission)
    {
        currentUser.RequireAuthenticated();
        if (!IsOwner(uploadedById, currentUser) && !currentUser.HasPermission(permission))
            throw new AppForbiddenException("You do not have permission to manage this image.");
    }

    public static bool IsOwner(int? uploadedById, IUserContext currentUser)
        => currentUser.UserId is not null && uploadedById == currentUser.UserId;

    public static ImageViewer GetViewer(IUserContext currentUser, bool includeReviewTags = false)
        => new(currentUser.UserId, currentUser.HasPermission(AccessPermission.ImagesReadRestricted) ||
                                   (includeReviewTags &&
                                    currentUser.HasPermission(AccessPermission.ImagesReviewQueue)));
}