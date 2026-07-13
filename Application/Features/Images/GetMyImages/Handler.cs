using Application.Helpers;
using Contracts;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetMyImages;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<Command, PageableImagesDto>
{
    public async Task<PageableImagesDto> Handle(Command request, CancellationToken cancellationToken)
    {
        currentUser.RequireAuthenticated();
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var result = await repositories.AppImage.GetUploadedByUserAsync(currentUser.UserId!.Value, request.IncludeHidden, page, pageSize, cancellationToken);
        return new PageableImagesDto(page, pageSize, result.Total, ImageSort.Newest,
            result.Images.Select(image => ImageDtoMapper.ToDto(image, currentUser)).ToList());
    }
}
