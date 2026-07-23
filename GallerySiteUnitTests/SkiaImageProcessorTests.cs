using Entities.Exceptions;
using Microsoft.Extensions.Options;
using Service;
using SkiaSharp;

namespace GallerySiteUnitTests;

public sealed class SkiaImageProcessorTests
{
    [Fact]
    public async Task InspectAsync_ValidJpeg_ReturnsActualFormatAndDimensions()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var bitmap = new SKBitmap(24, 12);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
            await File.WriteAllBytesAsync(path, data.ToArray());

            var result = await Processor().InspectAsync(path);

            Assert.Equal(24, result.Width);
            Assert.Equal(12, result.Height);
            Assert.Equal("image/jpeg", result.ContentType);
            Assert.Equal(".jpg", result.Extension);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InspectAsync_NonImage_ThrowsValidationError()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "not an image");
            await Assert.ThrowsAsync<ImageUploadValidationError>(() => Processor().InspectAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CreatePreviewJpegAsync_LargeImage_ResizesToMaximumLongEdgeAsJpeg()
    {
        await using var source = CreatePng(1600, 800, SKColors.Red);
        await using var preview = await Processor().CreatePreviewJpegAsync(source);
        using var codec = SKCodec.Create(preview);

        Assert.NotNull(codec);
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
        Assert.Equal(768, codec.Info.Width);
        Assert.Equal(384, codec.Info.Height);
    }

    [Fact]
    public async Task CreatePreviewJpegAsync_SmallImage_DoesNotUpscale()
    {
        await using var source = CreatePng(24, 12, SKColors.Blue);
        await using var preview = await Processor().CreatePreviewJpegAsync(source);
        using var codec = SKCodec.Create(preview);

        Assert.NotNull(codec);
        Assert.Equal(24, codec.Info.Width);
        Assert.Equal(12, codec.Info.Height);
    }

    [Fact]
    public async Task CreatePreviewJpegAsync_TransparentImage_CompositesOntoWhite()
    {
        await using var source = CreatePng(8, 8, SKColors.Transparent);
        await using var preview = await Processor().CreatePreviewJpegAsync(source);
        using var bitmap = SKBitmap.Decode(preview);

        Assert.NotNull(bitmap);
        var pixel = bitmap.GetPixel(0, 0);
        Assert.True(pixel.Red >= 245 && pixel.Green >= 245 && pixel.Blue >= 245);
    }

    [Fact]
    public async Task LocalImageStorage_SaveMoveReadDelete_PreservesBytesWithinConfiguredRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gallery-storage-{Guid.NewGuid():N}");
        try
        {
            var storage = new LocalImageStorage(Options.Create(new ImageStorageOptions { RootPath = root }));
            var bytes = "gallery image"u8.ToArray();
            await using var input = new MemoryStream(bytes);
            var temporaryPath = await storage.SaveTemporaryAsync(input);
            await storage.MoveTemporaryToFinalAsync(temporaryPath, "uploads/item.bin");

            await using var stored = await storage.OpenReadAsync("uploads/item.bin");
            using var output = new MemoryStream();
            await stored.CopyToAsync(output);
            Assert.Equal(bytes, output.ToArray());

            await storage.DeleteAsync("uploads/item.bin");
            Assert.False(storage.Exists("uploads/item.bin"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static SkiaImageProcessor Processor()
        => new(Options.Create(new ImageStorageOptions { RootPath = Path.GetTempPath(), MaximumUploadMegabytes = 20, MaximumPixels = 50_000_000 }));

    private static MemoryStream CreatePng(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Position = 0;
        return stream;
    }
}
