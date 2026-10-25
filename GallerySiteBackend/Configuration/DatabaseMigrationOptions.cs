using System.ComponentModel.DataAnnotations;

namespace GallerySiteBackend.Configuration;

public sealed class DatabaseMigrationOptions
{
    [Range(1, int.MaxValue)]
    public int CommandTimeoutSeconds { get; set; } = 600;
}
