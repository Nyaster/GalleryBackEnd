namespace Entities.Models;

public static class UploadAccess
{
    public static bool IsAllowed(AppUser user, bool bypassRestrictions)
        => bypassRestrictions ||
           (!user.UploadsBlocked && (user.CanUploadImages || user.AuthenticatorEnabledAtUtc is not null));
}
