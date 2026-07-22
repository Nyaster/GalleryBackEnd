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
        if (!request.AsJpeg || string.Equals(image.ContentType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
            return new ImageContent(await storage.OpenReadAsync(image.StorageKey, cancellationToken), image.ContentType);

        var stream = await derivativeCache.GetOrCreateAsync(image.StorageKey, ImageDerivativeVariants.JpegV1,
            processor.ConvertToJpegAsync, cancellationToken);
        return new ImageContent(stream, "image/jpeg");
    }
}

public sealed record ImageContent(Stream Stream, string ContentType);
