namespace Shared.DataTransferObjects;

public sealed record UploadPermissionDto(int Id, string Login, bool CanUploadImages);

public sealed record UpdateUploadPermissionDto(bool CanUploadImages);
