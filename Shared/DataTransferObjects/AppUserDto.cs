namespace Shared.DataTransferObjects;

public sealed record AppUserDto(int Id, string Login, IReadOnlyList<string> Roles);
