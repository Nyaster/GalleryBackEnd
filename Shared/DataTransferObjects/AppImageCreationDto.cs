using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Shared.DataTransferObjects;

public sealed class AppImageCreationDto
{
    [Required]
    public IFormFile? ImageFile { get; init; }
    public bool IsPrivate { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}
