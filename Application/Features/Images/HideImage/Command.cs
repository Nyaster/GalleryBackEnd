using MediatR;

namespace Application.Features.Images.HideImage;

public sealed record Command(int ImageId) : IRequest;
