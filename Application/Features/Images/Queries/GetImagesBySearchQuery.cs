using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.Queries;

public record GetImagesBySearchQuery(SearchImageDto SearchImageDto) : IRequest<PageableImagesDto>;