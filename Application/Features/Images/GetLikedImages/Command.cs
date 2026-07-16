using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetLikedImages;

public sealed record Command(int Page = 1, int PageSize = 20) : IRequest<PageableLikedImagesDto>;
