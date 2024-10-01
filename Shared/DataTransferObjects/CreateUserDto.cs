using System.ComponentModel.DataAnnotations;

namespace Shared.DataTransferObjects;

public record CreateUserDto([MinLength(4)] string Login, [MinLength(8)] string Password);