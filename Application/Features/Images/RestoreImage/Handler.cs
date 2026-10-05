using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.RestoreImage;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<Command, AppImageDto>
{
    public async Task<AppImageDto> Handle(Command request, CancellationToken cancellationToken)
    {
        var image = await repositories.AppImage.GetByIdAsync(request.ImageId, true, cancellationToken)
            ?? throw new Base404ReturnException("Image not found.");
        ImageAuthorization.EnsureCanManage(image, currentUser);
        image.DeletedAtUtc = null;
        image.ModerationStatus = ModerationStatus.Pending;
        await repositories.SaveAsync(cancellationToken);
        var card = await repositories.AppImage.GetCardByIdAsync(image.Id, ImageAuthorization.GetViewer(currentUser), cancellationToken)
            ?? throw new InvalidOperationException("The saved image could not be read.");
        return ImageDtoMapper.ToDto(card);
    }
}
