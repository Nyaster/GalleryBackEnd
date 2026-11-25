using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.GetUserUploadPermission;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser, IRoleAccessService roleAccess) : IRequestHandler<Command, UploadPermissionDto>
{
    public async Task<UploadPermissionDto> Handle(Command request, CancellationToken cancellationToken)
    {
        EnsureCanManageAccess(currentUser);
        var user = await repositories.AppUser.GetByIdAsync(request.UserId, false, cancellationToken)
            ?? throw new Base404ReturnException("User not found.");
        return await ToDtoAsync(user, roleAccess, cancellationToken);
    }

    internal static void EnsureCanManageAccess(IUserContext currentUser)
    {
        currentUser.RequireAuthenticated();
        if (!currentUser.HasPermission(AccessPermission.UploadsManageAccess))
            throw new AppForbiddenException("Upload access management permission is required.");
    }

    internal static async Task<UploadPermissionDto> ToDtoAsync(AppUser user, IRoleAccessService roleAccess,
        CancellationToken cancellationToken)
    {
        var access = await roleAccess.GetAccessAsync(user.Id, cancellationToken);
        var bypassRestrictions = access?.Permissions.Contains(AccessPermission.UploadsBypassRestrictions) == true;
        return new UploadPermissionDto(user.Id, user.Login, UploadAccess.IsAllowed(user, bypassRestrictions),
            user.CanUploadImages, user.AuthenticatorEnabledAtUtc is not null, user.UploadsBlocked);
    }
}
