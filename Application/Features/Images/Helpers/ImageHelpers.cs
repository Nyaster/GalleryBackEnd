using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Processing;

namespace Application.Features.Images.Helpers;

public static class ImageHelpers
{
    public static async Task<(int Width, int Height)> GetImageDimensionsAsync(string imagePath)
    {
        using var image = await Image.LoadAsync(imagePath);
        return (image.Width, image.Height);
    }

    public static (int Width, int Height) GetImageDimensions(string imagePath)
    {
        using var image = Image.Load(imagePath);
        return (image.Width, image.Height);
    }

    public static async Task<Stream> ConvertImageToJpeg(Stream file, CancellationToken cancellationToken)
    {
        var imageInfo = await Image.IdentifyAsync(file, cancellationToken);
        var targetWidth = imageInfo.Width / 3;
        var targetHeight = imageInfo.Height / 3;
        var decoderOption = new DecoderOptions()
        {
            TargetSize = new Size(targetWidth, targetHeight),
        };
        file.Position = 0;
        var image = await Image.LoadAsync(decoderOption, file, cancellationToken);
        var memoryStream = new MemoryStream();
        
        await image.SaveAsJpegAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;
        return memoryStream;
    }
}