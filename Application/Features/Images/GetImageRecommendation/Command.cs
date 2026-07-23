using MediatR;
using Shared.DataTransferObjects;
using Entities.Models;

namespace Application.Features.Images.GetImageRecommendation;

public sealed record Command(int Id, int Page = 1, int PageSize = 20, IReadOnlyList<AiUsageClassification>? AiUsage = null) : IRequest<PageableRecommendationsDto>;
