using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.PublishImage;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<Command, AppImageDto?>
{
    public async Task<AppImageDto?> Handle(Command request, CancellationToken cancellationToken)
    {
        await using var transaction = await repositories.BeginSerializableTransactionAsync(cancellationToken);
        var image = await repositories.AppImage.GetByIdAsync(request.ImageId, true, cancellationToken)
            ?? throw new Base404ReturnException("Image not found.");
        ImageAuthorization.EnsureOwnerOrPermission(image.UploadedById, currentUser, AccessPermission.ImagesPublishAny);
        ImageAuthorization.EnsureReadableForManagement(image, currentUser);
        if (image.Visibility != ImageVisibility.Private)
            throw new Base400BadRequestException("Only private images can be published.");

        image.Visibility = ImageVisibility.Gallery;
        await repositories.SaveAsync(cancellationToken);
        var response = await ImageAuthorization.GetManagementMutationResponseAsync(repositories.AppImage, image,
            currentUser, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }
}
