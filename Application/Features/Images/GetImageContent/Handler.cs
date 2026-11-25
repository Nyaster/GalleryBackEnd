using Entities.Models;
using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using MediatR;
using Service.Contracts;

namespace Application.Features.Images.GetImageContent;

public sealed class Handler(
    IRepositoryManager repositories,
    IImageStorage storage,
    IImageProcessor processor,
    IImageDerivativeCache derivativeCache,
    IUserContext currentUser)
    : IRequestHandler<Command, ImageContent>
{
    public async Task<ImageContent> Handle(Command request, CancellationToken cancellationToken)
    {
        ImageMetadata image;
        if (request.ForModeration)
        {
            currentUser.RequireAuthenticated();
            var canReadRestricted = currentUser.HasPermission(AccessPermission.ImagesReadRestricted);
            if (!canReadRestricted &&
                (!currentUser.HasPermission(AccessPermission.ImagesReviewQueue) ||
                 request.Format != ImageContentFormat.Preview))
                throw new AppForbiddenException("Only review previews are available with your permissions.");

            var metadata = canReadRestricted
                ? await repositories.AppImage.GetMetadataByIdAsync(request.Id, cancellationToken)
                : await repositories.AppImage.GetReviewMetadataByIdAsync(request.Id, cancellationToken);
            image = metadata ?? throw new Base404ReturnException("Image not found.");
        }
        else
        {
            image = await ImageAuthorization.GetReadableMetadataAsync(repositories.AppImage, request.Id,
                currentUser, cancellationToken);
        }

        if (!storage.Exists(image.StorageKey))
            throw new InvalidOperationException("Image data is unavailable.");

        return request.Format switch
        {
            ImageContentFormat.Original => new ImageContent(
                await storage.OpenReadAsync(image.StorageKey, cancellationToken), image.ContentType),
            ImageContentFormat.Jpeg when string.Equals(image.ContentType, "image/jpeg",
                    StringComparison.OrdinalIgnoreCase) =>
                new ImageContent(await storage.OpenReadAsync(image.StorageKey, cancellationToken), image.ContentType),
            ImageContentFormat.Jpeg => new ImageContent(
                await derivativeCache.GetOrCreateAsync(image.StorageKey, ImageDerivativeVariants.JpegV1,
                    processor.ConvertToJpegAsync, cancellationToken), "image/jpeg"),
            ImageContentFormat.Preview => new ImageContent(
                await derivativeCache.GetOrCreateAsync(image.StorageKey, ImageDerivativeVariants.PreviewJpeg768V1,
                    processor.CreatePreviewJpegAsync, cancellationToken), "image/jpeg"),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Format,
                "Unsupported image content format.")
        };
    }
}

public sealed record ImageContent(Stream Stream, string ContentType);