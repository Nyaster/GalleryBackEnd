namespace Service.Contracts;

public interface IImageEmbeddingGenerator
{
    Task<Pgvector.Vector> GenerateEmbeddingAsync(Stream imageStream, CancellationToken cancellationToken);
}