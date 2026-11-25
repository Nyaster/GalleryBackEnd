using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetImageTagChanges;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser)
    : IRequestHandler<Command, PageableImageTagChangesDto>
{
    public async Task<PageableImageTagChangesDto> Handle(Command request, CancellationToken cancellationToken)
    {
        var image = await repositories.AppImage.GetMetadataByIdAsync(request.ImageId, cancellationToken)
                    ?? throw new Base404ReturnException("Image not found.");
        currentUser.RequireAuthenticated();
        if (!ImageAuthorization.IsOwner(image.UploadedById, currentUser) &&
            !currentUser.HasPermission(AccessPermission.ImageTagsReview))
            throw new AppForbiddenException("You do not have permission to view tag changes for this image.");
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var result =
            await repositories.ImageTagChanges.GetAsync(image.Id, null, null, page, pageSize, cancellationToken);
        return new PageableImageTagChangesDto(page, pageSize, result.Total,
            result.Changes.Select(ImageTagChangeMapper.ToDto).ToArray());
    }
}