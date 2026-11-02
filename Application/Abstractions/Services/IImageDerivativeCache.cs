namespace Service.Contracts;

public interface IImageDerivativeCache
{
    Task<Stream> GetOrCreateAsync(
        string sourceStorageKey,
        ImageDerivativeVariant variant,
        Func<Stream, CancellationToken, Task<Stream>> factory,
        CancellationToken cancellationToken = default);
}

public sealed record ImageDerivativeVariant(string CacheKey, string Extension);

public static class ImageDerivativeVariants
{
    public static readonly ImageDerivativeVariant JpegV1 = new("jpeg-v1", ".jpg");
    public static readonly ImageDerivativeVariant PreviewJpeg768V1 = new("preview-jpeg-768-v1", ".jpg");
}
