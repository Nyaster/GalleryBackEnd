using MediatR;

namespace Application.Features.Images.GenerateImageEmbedding;

public record Command(int imageId) : IRequest;