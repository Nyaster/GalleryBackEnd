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
}
