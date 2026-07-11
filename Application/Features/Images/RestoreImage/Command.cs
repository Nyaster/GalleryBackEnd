using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.RestoreImage;

public sealed record Command(int ImageId) : IRequest<AppImageDto>;
