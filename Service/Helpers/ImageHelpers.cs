using System.Security.Cryptography;
using SixLabors.ImageSharp;

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
        var memoryStream = new MemoryStream();
        await image.SaveAsJpegAsync(memoryStream);
        var buffer = memoryStream.ToArray();
        return buffer;
    }
}