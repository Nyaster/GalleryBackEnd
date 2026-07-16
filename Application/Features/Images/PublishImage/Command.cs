using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.PublishImage;

public sealed record Command(int ImageId) : IRequest<AppImageDto>;
