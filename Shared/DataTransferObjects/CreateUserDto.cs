using System.ComponentModel.DataAnnotations;

namespace Shared.DataTransferObjects;

public sealed record CreateUserDto(
    [property: Required, StringLength(64, MinimumLength = 4)] string Login,
    [property: Required, StringLength(128, MinimumLength = 12)] string Password);
