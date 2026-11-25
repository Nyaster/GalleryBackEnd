using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.GetPendingImages;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser)
    : IRequestHandler<Command, List<AppImageDto>>
{
    public async Task<List<AppImageDto>> Handle(Command request, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(AccessPermission.ImagesReviewQueue))
            throw new AppForbiddenException("Image review queue permission is required.");
        var images = await repositories.AppImage.GetPendingAsync(request.Page, request.PageSize,
            ImageAuthorization.GetViewer(currentUser, includeReviewTags: true), cancellationToken);
        return images.Select(image => ImageDtoMapper.ToModerationDto(image, currentUser)).ToList();
    }
}