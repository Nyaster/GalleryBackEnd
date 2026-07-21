using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetImageRecommendation;

public sealed record Command(int Id, int Page = 1, int PageSize = 20) : IRequest<PageableRecommendationsDto>;
