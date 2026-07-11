using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using MediatR;
using Service.Contracts;

namespace Application.Features.Images.HideImage;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser, TimeProvider clock) : IRequestHandler<Command>
{
    public async Task Handle(Command request, CancellationToken cancellationToken)
    {
        var image = await repositories.AppImage.GetByIdAsync(request.ImageId, true, cancellationToken)
            ?? throw new Base404ReturnException("Image not found.");
        ImageAuthorization.EnsureCanManage(image, currentUser);
        image.DeletedAtUtc ??= clock.GetUtcNow();
        await repositories.SaveAsync(cancellationToken);
    }
}
