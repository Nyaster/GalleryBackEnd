namespace Entities.Models;

public sealed class ImageLike
{
    public int ImageId { get; set; }
    public AppImage? Image { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class ImageLikeActivity
{
    public long Id { get; set; }
    public int ImageId { get; set; }
    public int UserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public LikeActivityType Type { get; set; }
}

public enum LikeActivityType
{
    Like,
    Unlike
}
