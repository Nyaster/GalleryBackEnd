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
    public async Task CreatePreviewJpegAsync_TransparentImage_CompositesOntoWhite()
    {
        await using var source = CreatePng(8, 8, SKColors.Transparent);
        await using var preview = await Processor().CreatePreviewJpegAsync(source);
        using var bitmap = SKBitmap.Decode(preview);

        Assert.NotNull(bitmap);
        var pixel = bitmap.GetPixel(0, 0);
        Assert.True(pixel.Red >= 245 && pixel.Green >= 245 && pixel.Blue >= 245);
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg, 6000, 4000, 768, 512)]
    [InlineData(SKEncodedImageFormat.Jpeg, 4001, 6001, 512, 768)]
    [InlineData(SKEncodedImageFormat.Jpeg, 1601, 801, 768, 384)]
    [InlineData(SKEncodedImageFormat.Jpeg, 4000, 3, 768, 1)]
    [InlineData(SKEncodedImageFormat.Jpeg, 769, 513, 768, 512)]
    [InlineData(SKEncodedImageFormat.Webp, 1601, 801, 768, 384)]
    [InlineData(SKEncodedImageFormat.Webp, 801, 1601, 384, 768)]
    [InlineData(SKEncodedImageFormat.Png, 801, 1601, 384, 768)]
    [InlineData(SKEncodedImageFormat.Jpeg, 24, 12, 24, 12)]
    [InlineData(SKEncodedImageFormat.Webp, 24, 12, 24, 12)]
    public async Task CreatePreviewJpegAsync_SupportedSizes_PreservesDimensionsAndContent(
        SKEncodedImageFormat format, int width, int height, int expectedWidth, int expectedHeight)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint())
        {
            paint.Color = SKColors.Blue;
            canvas.Clear(SKColors.Red);
            canvas.DrawRect(SKRect.Create(width / 2f, 0, width / 2f, height), paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        using var source = new MemoryStream(data.ToArray());

        using var preview = await Processor().CreatePreviewJpegAsync(source);
        Assert.Equal(0, preview.Position);
        using var codec = SKCodec.Create(preview);
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
        using var result = SKBitmap.Decode(codec);
        Assert.Equal(expectedWidth, result.Width);
        Assert.Equal(expectedHeight, result.Height);
        var left = result.GetPixel(expectedWidth / 4, expectedHeight / 2);
        var right = result.GetPixel(3 * expectedWidth / 4, expectedHeight / 2);
        Assert.True(left.Red >= 235 && left.Blue <= 20);
        Assert.True(right.Blue >= 235 && right.Red <= 20);
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task CreatePreviewJpegAsync_PartialTransparency_CompositesOntoWhite(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(1601, 801, SKColorType.Bgra8888, SKAlphaType.Premul);
        bitmap.Erase(new SKColor(255, 0, 0, 128));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 100);
        using var source = new MemoryStream(data.ToArray());

        using var preview = await Processor().CreatePreviewJpegAsync(source);
        using var result = SKBitmap.Decode(preview);
        var pixel = result.GetPixel(result.Width / 2, result.Height / 2);
        Assert.InRange(pixel.Red, (byte)245, (byte)255);
        Assert.InRange(pixel.Green, (byte)117, (byte)137);
        Assert.InRange(pixel.Blue, (byte)117, (byte)137);
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task CreatePreviewJpegAsync_NonSeekableSource_ProducesPreviewAndPreservesDisposalBehavior(
        SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(1601, 801);
        bitmap.Erase(SKColors.Green);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        using var source = new NonSeekableStream(data.ToArray());

        using var preview = await Processor().CreatePreviewJpegAsync(source);
        using var result = SKBitmap.Decode(preview);
        Assert.Equal(768, result.Width);
        Assert.Equal(384, result.Height);
        Assert.True(source.Disposed);
    }

    [Fact]
    public async Task CreatePreviewJpegAsync_AnimatedWebp_UsesFirstFrame()
    {
        var bytes = CreateAnimatedWebp();
        using (var codec = SKCodec.Create(new MemoryStream(bytes)))
            Assert.Equal(2, codec.FrameCount);
        using var source = new MemoryStream(bytes);

        using var preview = await Processor().CreatePreviewJpegAsync(source);
        using var result = SKBitmap.Decode(preview);
        var pixel = result.GetPixel(16, 8);
        Assert.True(pixel.Red >= 235 && pixel.Blue <= 20);
    }

    [Fact]
    public async Task CreatePreviewJpegAsync_InvalidContent_ThrowsValidationError()
    {
        using var source = new MemoryStream("not an image"u8.ToArray());
        var error = await Assert.ThrowsAsync<ImageUploadValidationError>(() =>
            Processor().CreatePreviewJpegAsync(source));
        Assert.Equal("Image content cannot be decoded.", error.Message);
    }

    [Fact]
    public async Task CreatePreviewJpegAsync_CanceledToken_ThrowsBeforeReadingOrDisposingSource()
    {
        using var source = new NonSeekableStream([]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Processor().CreatePreviewJpegAsync(source, cancellation.Token));
        Assert.Equal(0, source.ReadCount);
        Assert.False(source.Disposed);
    }

    [Fact]
    public async Task LocalImageStorage_SaveMoveReadDelete_PreservesBytesWithinConfiguredRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gallery-storage-{Guid.NewGuid():N}");
        try
        {
            var storage = new LocalImageStorage(Options.Create(new ImageStorageOptions { RootPath = root }),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalImageStorage>.Instance);
            var bytes = "gallery image"u8.ToArray();
            await using var input = new MemoryStream(bytes);
            var temporaryPath = await storage.SaveTemporaryAsync(input);
            await storage.MoveTemporaryToFinalAsync(temporaryPath, "uploads/item.bin");
            await storage.DeleteTemporaryAsync(temporaryPath);
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.DeleteTemporaryAsync(
                Path.Combine(root, ".tmp", "..", "uploads", "item.bin")));

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
        => new(Options.Create(new ImageStorageOptions
            { RootPath = Path.GetTempPath(), MaximumUploadMegabytes = 20, MaximumPixels = 50_000_000 }));

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

    private static byte[] CreateAnimatedWebp()
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write("WEBP"u8);
        // Extended WebP header: animation flag and a 32x16 canvas (sizes minus one).
        WriteChunk("VP8X"u8, [2, 0, 0, 0, 31, 0, 0, 15, 0, 0]);
        WriteChunk("ANIM"u8, [255, 255, 255, 255, 0, 0]);
        foreach (var color in new[] { SKColors.Red, SKColors.Blue })
        {
            using var bitmap = new SKBitmap(32, 16);
            bitmap.Erase(color);
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, 100);
            var frame = encoded.ToArray();
            // Full-canvas frame, 20ms duration, no blending; append the VP8 chunk.
            byte[] header = [0, 0, 0, 0, 0, 0, 31, 0, 0, 15, 0, 0, 20, 0, 0, 2];
            WriteChunk("ANMF"u8, [.. header, .. frame.AsSpan(12)]);
        }

        using var output = new MemoryStream();
        using var container = new BinaryWriter(output);
        container.Write("RIFF"u8);
        container.Write((int)payload.Length);
        container.Write(payload.ToArray());
        return output.ToArray();

        void WriteChunk(ReadOnlySpan<byte> name, byte[] bytes)
        {
            writer.Write(name);
            writer.Write(bytes.Length);
            writer.Write(bytes);
            if (bytes.Length % 2 != 0) writer.Write((byte)0);
        }
    }

    private sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public bool Disposed { get; private set; }
        public int ReadCount { get; private set; }
        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCount++;
            return _inner.Read(buffer, offset, count);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
