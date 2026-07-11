using Application.Helpers;
using Contracts;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.GetPendingImages;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<Command, List<AppImageDto>>
{
    public async Task<List<AppImageDto>> Handle(Command request, CancellationToken cancellationToken)
    {
        if (!ImageAuthorization.IsStaff(currentUser)) throw new Entities.Exceptions.AppForbiddenException("Staff access is required.");
        var images = await repositories.AppImage.GetPendingAsync(request.Page, request.PageSize, cancellationToken);
        return images.Select(image => ImageDtoMapper.ToDto(image, currentUser)).ToList();
    }
}
