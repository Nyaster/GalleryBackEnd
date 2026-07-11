using MediatR;

namespace Application.Features.Images.GenerateImageEmbedding;

public sealed record Command(int ImageId) : IRequest;
