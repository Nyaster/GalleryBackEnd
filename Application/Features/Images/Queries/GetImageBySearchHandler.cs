using AutoMapper;
using Contracts;
using Entities.Models;
using GallerySiteBackend.Models;
using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.Queries;

public class GetImageBySearchHandler(IRepositoryManager repositoryManager, IMapper mapper)
    : IRequestHandler<GetImagesBySearchQuery, PageableImagesDto>
{
    public async Task<PageableImagesDto> Handle(GetImagesBySearchQuery request, CancellationToken cancellationToken)
    {
        var getImageRequest = request.SearchImageDto;
        List<ImageTag> list;
        if (getImageRequest.Tags == null)
            list = new List<ImageTag>();
        else
            list = await repositoryManager.AppImage.GetTagsByNames(getImageRequest.Tags);


        var pageNumber = getImageRequest.Page;
        if (getImageRequest.Page < 1) pageNumber = 1;

        var pageSize = getImageRequest.PageSize;
        if (getImageRequest.PageSize is < 1 or > 20) pageSize = 10;

        pageNumber -= 1;
        var searchImagesByTags =
            await repositoryManager.AppImage.SearchImagesByTags(list, OrderBy.Id, pageNumber, pageSize);
        var page = new PageableImagesDto
        {
            Images = searchImagesByTags.images.Select(mapper.Map<AppImage, AppImageDto>).ToList(),
            OrderBy = OrderBy.Id.ToString(),
            Page = getImageRequest.Page,
            PageSize = pageSize,
            Total = searchImagesByTags.total
        };
        return page;
    }
}