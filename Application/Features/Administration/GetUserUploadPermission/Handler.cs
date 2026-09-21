using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.GetUserUploadPermission;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<Command, UploadPermissionDto>
{
    public async Task<UploadPermissionDto> Handle(Command request, CancellationToken cancellationToken)
    {
        EnsureAdministrator(currentUser);
        var user = await repositories.AppUser.GetByIdAsync(request.UserId, false, cancellationToken)
            ?? throw new Base404ReturnException("User not found.");
        return ToDto(user);
    }

    internal static void EnsureAdministrator(IUserContext currentUser)
    {
        if (!currentUser.IsInRole(nameof(AppUserRole.Admin)))
            throw new AppForbiddenException("Administrator access is required.");
    }

    internal static UploadPermissionDto ToDto(AppUser user)
        => new(user.Id, user.Login, UploadAccess.IsAllowed(user), user.CanUploadImages,
            user.AuthenticatorEnabledAtUtc is not null, user.UploadsBlocked);
}
