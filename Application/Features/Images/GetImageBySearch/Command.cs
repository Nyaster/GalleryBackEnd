using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetImageBySearch;

public record Command(SearchImageDto SearchImageDto) : IRequest<PageableImagesDto>;