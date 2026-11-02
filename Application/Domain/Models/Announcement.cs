namespace Entities.Models;

public sealed class Announcement
{
    // The application has exactly one announcement row, identified by this fixed key.
    public int Id { get; set; }
    public required string Content { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public int UpdatedByUserId { get; set; }
    public AppUser? UpdatedByUser { get; set; }
}

public sealed class Feedback
{
    public int Id { get; set; }
    public int AuthorId { get; set; }
    public AppUser? Author { get; set; }
    public required string Content { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
