namespace Entities.Models;

public sealed class Comment
{
    public int Id { get; set; }
    public int ImageId { get; set; }
    public AppImage? Image { get; set; }
    public int AuthorId { get; set; }
    public AppUser? Author { get; set; }
    public required string Content { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public int? DeletedByUserId { get; set; }
}

public sealed class CommentRestriction
{
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    // A null value means permanently restricted; the row's existence is significant.
    public DateTimeOffset? RestrictedUntilUtc { get; set; }
    public int RestrictedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
