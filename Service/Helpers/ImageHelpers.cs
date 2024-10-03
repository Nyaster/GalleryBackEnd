using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Service.Helpers;

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

    public static async Task<string> GetSha256Hash(string imagePath)
    {
        using var sha256 = SHA256.Create();
        await using var image = File.OpenRead(imagePath);
        var computeHashAsync = await sha256.ComputeHashAsync(image);
        return BitConverter.ToString(computeHashAsync).Replace("-", string.Empty).ToLowerInvariant();
    }

    public static async Task<byte[]> ConvertImageToJpeg(string imagePath)
    {
        using var image = await Image.LoadAsync(imagePath);
        using var memoryStream = new MemoryStream();
        image.Mutate(context =>
        {
            context.Resize(image.Width/3, image.Height/3 );
        });
        await image.SaveAsJpegAsync(memoryStream);
        var buffer = memoryStream.ToArray();
        return buffer;
    }
}