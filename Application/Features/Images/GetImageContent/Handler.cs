using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using MediatR;
using Service.Contracts;

namespace Application.Features.Images.GetImageContent;

public sealed class Handler(IRepositoryManager repositories, IImageStorage storage, IImageProcessor processor,
    IImageDerivativeCache derivativeCache, IUserContext currentUser)
    : IRequestHandler<Command, ImageContent>
{
    public async Task<ImageContent> Handle(Command request, CancellationToken cancellationToken)
    {
        var image = await repositories.AppImage.GetByIdAsync(request.Id, false, cancellationToken)
            ?? throw new Base404ReturnException("Image not found.");
        ImageAuthorization.EnsureReadable(image, currentUser);
        if (!storage.Exists(image.StorageKey))
            throw new InvalidOperationException("Image data is unavailable.");

        return request.Format switch
        {
            ImageContentFormat.Original => new ImageContent(
                await storage.OpenReadAsync(image.StorageKey, cancellationToken), image.ContentType),
            ImageContentFormat.Jpeg when string.Equals(image.ContentType, "image/jpeg", StringComparison.OrdinalIgnoreCase) =>
                new ImageContent(await storage.OpenReadAsync(image.StorageKey, cancellationToken), image.ContentType),
            ImageContentFormat.Jpeg => new ImageContent(
                await derivativeCache.GetOrCreateAsync(image.StorageKey, ImageDerivativeVariants.JpegV1,
                    processor.ConvertToJpegAsync, cancellationToken), "image/jpeg"),
            ImageContentFormat.Preview => new ImageContent(
                await derivativeCache.GetOrCreateAsync(image.StorageKey, ImageDerivativeVariants.PreviewJpeg768V1,
                    processor.CreatePreviewJpegAsync, cancellationToken), "image/jpeg"),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Format, "Unsupported image content format.")
        };
    }
}

public sealed record ImageContent(Stream Stream, string ContentType);
