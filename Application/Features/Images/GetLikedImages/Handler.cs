using Application.Helpers;
using Contracts;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetLikedImages;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<Command, PageableLikedImagesDto>
{
    public async Task<PageableLikedImagesDto> Handle(Command request, CancellationToken cancellationToken)
    {
        currentUser.RequireAuthenticated();
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var result = await repositories.AppImage.GetLikedByUserAsync(currentUser.UserId!.Value, page, pageSize, cancellationToken);
        return new PageableLikedImagesDto(page, pageSize, result.Total,
            result.Images.Select(image => ImageDtoMapper.ToDto(image, currentUser)).ToList());
    }
}
