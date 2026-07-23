using MediatR;
using Shared.DataTransferObjects;
using Entities.Models;

namespace Application.Features.Images.GetLikedImages;

public sealed record Command(int Page = 1, int PageSize = 20, IReadOnlyList<AiUsageClassification>? AiUsage = null) : IRequest<PageableLikedImagesDto>;
