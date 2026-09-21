namespace Entities.Models;

public static class UploadAccess
{
    public static bool IsAllowed(AppUser user)
        => user.Roles.Contains(AppUserRole.Admin) ||
           (!user.UploadsBlocked && (user.CanUploadImages || user.AuthenticatorEnabledAtUtc is not null));
}