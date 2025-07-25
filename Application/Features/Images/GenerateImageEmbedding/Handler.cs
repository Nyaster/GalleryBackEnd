using Contracts;
using MediatR;
using Service.Contracts;

namespace Application.Features.Images.GenerateImageEmbedding;

public class Handler(IRepositoryManager repositoryManager, IImageEmbeddingGenerator embeddingGenerator)
    : IRequestHandler<Command>
{
    private readonly IRepositoryManager _repositoryManager = repositoryManager;
    private IImageEmbeddingGenerator _embeddingGenerator = embeddingGenerator;

    public async Task Handle(GenerateImageEmbedding.Command request, CancellationToken cancellationToken)
    {
        var image = await _repositoryManager.AppImage.GetById(request.imageId);
        if (image is null)
        {
            return;
        }

        if (!File.Exists(image.PathToFileOnDisc))
        {
            return;
        }

        try
        {
            await using var stream = new FileStream(image.PathToFileOnDisc, FileMode.Open, FileAccess.Read);
            var embeddingVector = await _embeddingGenerator.GenerateEmbeddingAsync(stream, cancellationToken);
            image.Embedding = embeddingVector;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error happened in image {image.Id}: {e.Message}");
            image.IsHidden = true;
            
        }
        finally
        {
            await _repositoryManager.Save();
        }
    }
}