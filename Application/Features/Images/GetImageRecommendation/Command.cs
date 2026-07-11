using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetImageRecommendation;

public sealed record Command(int Id, int Limit = 20) : IRequest<List<AppImageDto>>;
