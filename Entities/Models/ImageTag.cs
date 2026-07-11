namespace Entities.Models;

public sealed class ImageTag
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public List<AppImage> AppImages { get; set; } = [];
}
