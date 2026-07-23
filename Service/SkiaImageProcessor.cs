using Entities.Exceptions;
using Microsoft.Extensions.Options;
using Service.Contracts;
using SkiaSharp;

namespace Service;

public sealed class SkiaImageProcessor(IOptions<ImageStorageOptions> options) : IImageProcessor
{
    private const int PreviewMaximumLongEdge = 768;
    private const int PreviewJpegQuality = 80;

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

    public Task<Stream> CreatePreviewJpegAsync(Stream source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var bitmap = SKBitmap.Decode(source) ?? throw new ImageUploadValidationError("Image content cannot be decoded.");
        var scale = Math.Min(1d, PreviewMaximumLongEdge / (double)Math.Max(bitmap.Width, bitmap.Height));
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale, MidpointRounding.AwayFromZero));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale, MidpointRounding.AwayFromZero));
        var imageInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var surface = SKSurface.Create(imageInfo) ?? throw new ImageUploadValidationError("Image preview cannot be created.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        using var sourceImage = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(sourceImage, SKRect.Create(width, height), new SKSamplingOptions(SKFilterMode.Linear), null);

        using var preview = surface.Snapshot();
        using var encoded = preview.Encode(SKEncodedImageFormat.Jpeg, PreviewJpegQuality);
        var output = new MemoryStream();
        encoded.SaveTo(output);
        output.Position = 0;
        return Task.FromResult<Stream>(output);
    }
}
