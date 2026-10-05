using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetImageById;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<Command, AppImageDto>
{
    public async Task<AppImageDto> Handle(Command request, CancellationToken cancellationToken)
    {
        var image = await repositories.AppImage.GetCardByIdAsync(request.Id, ImageAuthorization.GetViewer(currentUser), cancellationToken)
            ?? throw new Base404ReturnException("Image not found.");
        ImageAuthorization.EnsureReadable(image, currentUser);
        return ImageDtoMapper.ToDto(image);
    }
}
