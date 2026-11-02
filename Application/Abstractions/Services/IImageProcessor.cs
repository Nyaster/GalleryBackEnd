namespace Service.Contracts;

public interface IImageProcessor
{
    Task<InspectedImage> InspectAsync(string temporaryPath, CancellationToken cancellationToken = default);
    Task<Stream> ConvertToJpegAsync(Stream source, CancellationToken cancellationToken = default);
    Task<Stream> CreatePreviewJpegAsync(Stream source, CancellationToken cancellationToken = default);
}

public sealed record InspectedImage(int Width, int Height, string ContentType, string Extension);
