using System.ComponentModel.DataAnnotations;

namespace GallerySiteBackend;

public sealed class GuestBrowsingOptions
{
    [Range(1, 100000)] public int RequestsPerMinute { get; init; } = 300;
}
