using System.ComponentModel.DataAnnotations;

namespace Shared.DataTransferObjects;

public record CreateUserDto(string Login, [MinLength(8)] string Password);