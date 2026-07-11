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
        if (!currentUser.IsInRole(nameof(AppUserRole.Admin))) throw new Entities.Exceptions.AppForbiddenException("Administrator access is required.");
        var images = await repositories.AppImage.GetPendingAsync(request.Page, request.PageSize, cancellationToken);
        return images.Select(ImageDtoMapper.ToDto).ToList();
    }
}
