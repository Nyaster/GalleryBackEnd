using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.GetHiddenImages;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<Command, List<AppImageDto>>
{
    public async Task<List<AppImageDto>> Handle(Command request, CancellationToken cancellationToken)
    {
        if (!ImageAuthorization.IsStaff(currentUser)) throw new AppForbiddenException("Staff access is required.");
        var images = await repositories.AppImage.GetHiddenAsync(request.Page, request.PageSize, ImageAuthorization.GetViewer(currentUser), cancellationToken);
        return images.Select(image => ImageDtoMapper.ToDto(image)).ToList();
    }
}
