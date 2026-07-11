using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetMyImages;

public sealed record Command : IRequest<List<AppImageDto>>;
