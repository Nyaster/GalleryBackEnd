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
        if (!ImageAuthorization.IsStaff(currentUser)) throw new AppForbiddenException("Staff access is required.");
        var image = await repositories.AppImage.GetByIdAsync(request.ImageId, true, cancellationToken)
            ?? throw new Base404ReturnException("Image not found.");
        if (image.DeletedAtUtc is not null) throw new Base404ReturnException("Image not found.");
        if (request.AiUsage == AiUsageClassification.Unknown)
            throw new Base400BadRequestException("aiUsage cannot be Unknown.");
        image.ModerationStatus = request.Status;
        if (request.AiUsage is not null)
            image.AiUsage = request.AiUsage.Value;
        await repositories.SaveAsync(cancellationToken);
        var card = await repositories.AppImage.GetCardByIdAsync(image.Id, ImageAuthorization.GetViewer(currentUser), cancellationToken)
            ?? throw new InvalidOperationException("The saved image could not be read.");
        return ImageDtoMapper.ToDto(card);
    }
}
