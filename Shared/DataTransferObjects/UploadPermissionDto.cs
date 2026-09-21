namespace Shared.DataTransferObjects;

public sealed record UploadPermissionDto(
    int Id,
    string Login,
    bool CanUploadImages,
    bool UploadPermissionGranted = false,
    bool AuthenticatorEnabled = false,
    bool UploadsBlocked = false);

public sealed record UpdateUploadPermissionDto(bool CanUploadImages, bool? UploadsBlocked = null);