using System.ComponentModel.DataAnnotations;

namespace Shared.DataTransferObjects;

public sealed record CreateUserDto(
    [param: Required, StringLength(64, MinimumLength = 4)] string Login,
    [param: Required, StringLength(128, MinimumLength = 12)] string Password);
