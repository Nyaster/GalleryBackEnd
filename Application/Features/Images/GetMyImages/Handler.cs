using Application.Helpers;
using Contracts;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetMyImages;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<Command, List<AppImageDto>>
{
    public async Task<List<AppImageDto>> Handle(Command request, CancellationToken cancellationToken)
    {
        currentUser.RequireAuthenticated();
        var images = await repositories.AppImage.GetUploadedByUserAsync(currentUser.UserId!.Value, cancellationToken);
        return images.Select(ImageDtoMapper.ToDto).ToList();
    }
}
