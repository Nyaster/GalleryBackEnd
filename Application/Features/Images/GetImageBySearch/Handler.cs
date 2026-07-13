using Application.Helpers;
using Contracts;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetImageBySearch;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<Command, PageableImagesDto>
{
    public async Task<PageableImagesDto> Handle(Command request, CancellationToken cancellationToken)
    {
        var normalized = request.Request with { Page = Math.Max(request.Request.Page, 1), PageSize = Math.Clamp(request.Request.PageSize, 1, 50) };
        var result = await repositories.AppImage.SearchAsync(normalized, cancellationToken);
        return new PageableImagesDto(normalized.Page, normalized.PageSize, result.Total, normalized.Sort,
            result.Images.Select(image => ImageDtoMapper.ToDto(image, currentUser)).ToArray());
    }
}
