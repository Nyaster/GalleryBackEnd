using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetImageRecommendation;

public record Command(int id) : IRequest<List<AppImageDto>>;