using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Entities.Models;

namespace Shared.DataTransferObjects;

public sealed class AppImageCreationDto
{
    [Required]
    public IFormFile? ImageFile { get; init; }
    public AiUsageClassification AiUsage { get; init; }
    public bool IsPrivate { get; init; }
    public List<string> Tags { get; init; } = [];
}
