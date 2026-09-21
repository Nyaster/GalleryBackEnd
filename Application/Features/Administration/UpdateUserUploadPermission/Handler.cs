using Contracts;
using Entities.Exceptions;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.UpdateUserUploadPermission;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<Command, UploadPermissionDto>
{
    public async Task<UploadPermissionDto> Handle(Command request, CancellationToken cancellationToken)
    {
        GetUserUploadPermission.Handler.EnsureAdministrator(currentUser);
        var user = await repositories.AppUser.GetByIdAsync(request.UserId, true, cancellationToken)
            ?? throw new Base404ReturnException("User not found.");
        user.CanUploadImages = request.CanUploadImages;
        if (request.UploadsBlocked is { } blocked)
            user.UploadsBlocked = blocked;
        await repositories.SaveAsync(cancellationToken);
        return GetUserUploadPermission.Handler.ToDto(user);
    }
}
