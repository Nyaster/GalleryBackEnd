using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.ChangeModeration;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<Command, AppImageDto>
{
    public async Task<AppImageDto> Handle(Command request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsInRole(nameof(AppUserRole.Admin))) throw new AppForbiddenException("Administrator access is required.");
        var image = await repositories.AppImage.GetByIdAsync(request.ImageId, true, cancellationToken)
            ?? throw new Base404ReturnException("Image not found.");
        if (image.DeletedAtUtc is not null) throw new Base404ReturnException("Image not found.");
        image.ModerationStatus = request.Status;
        await repositories.SaveAsync(cancellationToken);
        return ImageDtoMapper.ToDto(image);
    }
}
