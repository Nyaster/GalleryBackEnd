using Entities.Exceptions;
using Microsoft.Extensions.Options;
using Service.Contracts;
using SkiaSharp;

namespace Service;

public sealed class SkiaImageProcessor(IOptions<ImageStorageOptions> options) : IImageProcessor
{
    public Task<InspectedImage> InspectAsync(string temporaryPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fileInfo = new FileInfo(temporaryPath);
        if (fileInfo.Length == 0)
            throw new ImageUploadValidationError("The uploaded file is empty.");
        if (fileInfo.Length > options.Value.MaximumUploadMegabytes * 1024L * 1024L)
            throw new ImageUploadValidationError($"Image size may not exceed {options.Value.MaximumUploadMegabytes} MB.");

        using var codec = SKCodec.Create(temporaryPath);
        if (codec is null)
            throw new ImageUploadValidationError("The upload is not a supported image.");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > options.Value.MaximumPixels)
            throw new ImageUploadValidationError("Image dimensions exceed the allowed limit.");

        return Task.FromResult(codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => new InspectedImage(info.Width, info.Height, "image/jpeg", ".jpg"),
            SKEncodedImageFormat.Png => new InspectedImage(info.Width, info.Height, "image/png", ".png"),
            SKEncodedImageFormat.Webp => new InspectedImage(info.Width, info.Height, "image/webp", ".webp"),
            _ => throw new ImageUploadValidationError("Only JPEG, PNG, and WebP images are supported.")
        });
    }

    public Task<Stream> ConvertToJpegAsync(Stream source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var bitmap = SKBitmap.Decode(source) ?? throw new ImageUploadValidationError("Image content cannot be decoded.");
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        var output = new MemoryStream();
        encoded.SaveTo(output);
        output.Position = 0;
        return Task.FromResult<Stream>(output);
    }
}
