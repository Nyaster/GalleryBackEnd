using Pgvector;
using Service.Contracts;

namespace Service;

/// <summary>
/// Keeps the MediatR handler graph valid when embedding generation is disabled.
/// The embedding worker is not registered in this mode, so this implementation
/// should not normally be called.
/// </summary>
public sealed class DisabledImageEmbeddingGenerator : IImageEmbeddingGenerator
{
    public Task<Vector> GenerateEmbeddingAsync(Stream imageStream, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Image embedding generation is disabled.");
}
