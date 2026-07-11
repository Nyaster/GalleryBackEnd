using System.ComponentModel.DataAnnotations;

namespace Service;

public sealed class ImageStorageOptions
{
    [Required] public string RootPath { get; init; } = "Data/images";
    [Range(1, 100)] public int MaximumUploadMegabytes { get; init; } = 20;
    [Range(1, 100_000_000)] public int MaximumPixels { get; init; } = 50_000_000;
}
