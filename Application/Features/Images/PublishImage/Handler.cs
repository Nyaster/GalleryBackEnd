using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.PublishImage;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<Command, AppImageDto>
{
    public async Task<AppImageDto> Handle(Command request, CancellationToken cancellationToken)
    {
        var image = await repositories.AppImage.GetByIdAsync(request.ImageId, true, cancellationToken)
            ?? throw new Base404ReturnException("Image not found.");
        ImageAuthorization.EnsureCanManage(image, currentUser);
        if (image.Visibility != ImageVisibility.Private)
            throw new Base400BadRequestException("Only private images can be published.");

        image.Visibility = ImageVisibility.Gallery;
        await repositories.SaveAsync(cancellationToken);
        return ImageDtoMapper.ToDto(image, currentUser);
    }
}
