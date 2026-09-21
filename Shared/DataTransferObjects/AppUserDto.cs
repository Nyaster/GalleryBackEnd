namespace Shared.DataTransferObjects;

public sealed record AppUserDto(
    int Id,
    string Login,
    IReadOnlyList<string> Roles,
    bool AuthenticatorEnabled = false,
    bool CanUploadImages = false,
    bool UploadsBlocked = false,
    int BackupCodesRemaining = 0)
{
    public static AppUserDto FromUser(Entities.Models.AppUser user)
        => new(user.Id, user.Login, user.Roles.Select(role => role.ToString()).ToArray(),
            user.AuthenticatorEnabledAtUtc is not null, Entities.Models.UploadAccess.IsAllowed(user),
            user.UploadsBlocked, user.AuthenticatorBackupCodeHashes.Count);
}