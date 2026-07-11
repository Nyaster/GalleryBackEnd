using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetImageBySearch;

public sealed record Command(SearchImageDto Request) : IRequest<PageableImagesDto>;
