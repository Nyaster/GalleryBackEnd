using System.ComponentModel.DataAnnotations;

namespace Shared.Configuration;

public sealed class ForMeFeedOptions
{
    [Range(1, 100)]
    public int RepresentativeCount { get; init; } = 25;
}
