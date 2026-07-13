using System.ComponentModel.DataAnnotations;

namespace GallerySiteBackend;

public sealed class ObservabilityOptions
{
    [Required]
    public string LogPath { get; init; } = "/app/logs";

    [Range(1, 3650)]
    public int RetentionDays { get; init; } = 30;

    [Range(1, 600000)]
    public int SlowRequestMilliseconds { get; init; } = 1000;
}
