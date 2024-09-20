using System.ComponentModel.DataAnnotations;

namespace Shared.DataTransferObjects;

public record AppLoginDto([Required] string Login, [Required] string Password);