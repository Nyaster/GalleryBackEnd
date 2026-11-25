using Entities.Models;

namespace Shared.DataTransferObjects;

public sealed record AppUserDto(
    int Id,
    string Login,
    IReadOnlyList<string> Roles,
    bool AuthenticatorEnabled = false,
    bool CanUploadImages = false,
    bool UploadsBlocked = false,
    int BackupCodesRemaining = 0,
    bool DisplayAllContent = false)
{
    public IReadOnlyList<string> Permissions { get; init; } = [];

    public static AppUserDto FromUser(AppUser user, AccountAccessDto access)
        => new(user.Id, user.Login, access.Roles, user.AuthenticatorEnabledAtUtc is not null,
            UploadAccess.IsAllowed(user, access.Permissions.Contains(AccessPermission.UploadsBypassRestrictions)),
            user.UploadsBlocked, user.AuthenticatorBackupCodeHashes.Count, user.DisplayAllContent)
        { Permissions = access.Permissions };

}
