using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetImageById;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser, IImageViewRepository views)
    : IRequestHandler<Command, AppImageDto>
{
    public async Task<AppImageDto> Handle(Command request, CancellationToken cancellationToken)
    {
        var image = await repositories.AppImage.GetCardByIdAsync(request.Id, ImageAuthorization.GetViewer(currentUser),
                        cancellationToken)
                    ?? throw new Base404ReturnException("Image not found.");
        ImageAuthorization.EnsureReadable(image, currentUser);
        var totals = await views.GetTotalsAsync(image.Id, cancellationToken);
        return ImageDtoMapper.ToDto(image, currentUser) with
        {
            TotalViews = totals.TotalViews,
            UniqueRegisteredViewers = totals.UniqueRegisteredViewers
        };
    }
}