using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using MediatR;
using Service.Contracts;

namespace Application.Features.Images.GetImageContent;

public sealed class Handler(IRepositoryManager repositories, IImageStorage storage, IImageProcessor processor, IUserContext currentUser,
    SemaphoreSlim jpegConversionLimiter)
    : IRequestHandler<Command, ImageContent>
{
    public async Task<ImageContent> Handle(Command request, CancellationToken cancellationToken)
    {
        var image = await repositories.AppImage.GetByIdAsync(request.Id, false, cancellationToken)
            ?? throw new Base404ReturnException("Image not found.");
        ImageAuthorization.EnsureReadable(image, currentUser);
        if (!storage.Exists(image.StorageKey))
            throw new InvalidOperationException("Image data is unavailable.");
        var stream = await storage.OpenReadAsync(image.StorageKey, cancellationToken);
        if (!request.AsJpeg)
            return new ImageContent(stream, image.ContentType);
        await jpegConversionLimiter.WaitAsync(cancellationToken);
        try
        {
            await using (stream)
                return new ImageContent(await processor.ConvertToJpegAsync(stream, cancellationToken), "image/jpeg");
        }
        finally { jpegConversionLimiter.Release(); }
    }
}

public sealed record ImageContent(Stream Stream, string ContentType);
