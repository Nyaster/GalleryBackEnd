using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.ReplaceImageTags;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser, TimeProvider clock) : IRequestHandler<Command, AppImageDto>
{
    public async Task<AppImageDto> Handle(Command request, CancellationToken cancellationToken)
    {
        var image = await repositories.AppImage.GetByIdAsync(request.ImageId, true, cancellationToken)
            ?? throw new Base404ReturnException("Image not found.");
        ImageAuthorization.EnsureCanManage(image, currentUser);

        var tags = await repositories.AppImage.GetOrCreateTagsAsync(request.Request.Tags ?? [], clock.GetUtcNow(), cancellationToken);
        if (tags.Any(tag => tag.ModerationStatus == TagModerationStatus.Rejected))
            throw new Base400BadRequestException("Rejected tags cannot be used.");

        image.Tags = tags;
        if (!ImageAuthorization.IsStaff(currentUser))
            image.ModerationStatus = ModerationStatus.Pending;
        await repositories.SaveAsync(cancellationToken);
        return ImageDtoMapper.ToDto(image, currentUser);
    }
}
